"""Regresjoner for lisenskontrollen med et lite, syntetisk repo; ingen nettverk eller NuGet-restore."""
import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('license_check', ROOT / 'scripts/check-licenses.py')
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class LicenseCheckTests(unittest.TestCase):
    def setUp(self):
        temp_base = ROOT / 'output/license-tests'
        temp_base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=temp_base)
        self.root = Path(self.temp.name).resolve()
        # Kontroller slettemålet før TemporaryDirectory får rydde rekursivt etter testen.
        assert self.root.is_relative_to(temp_base.resolve()) and self.root != temp_base.resolve()
        self.addCleanup(self.temp.cleanup)
        self.project = self.root / 'src/GenericPopulation'
        self.cache = self.root / 'cache/test.package/1.0.0'
        for path in (self.project / 'obj', self.cache, self.root / 'LICENSES'):
            path.mkdir(parents=True)
        for name in checker.DOCUMENTS:
            file = self.root / name
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text('Synthetic documentation\n', encoding='utf-8')
        (self.root / 'LICENSES/test.txt').write_text('Synthetic copyright and permission\n', encoding='utf-8')
        shutil.copyfile(self.root / 'LICENSES/test.txt', self.cache / 'LICENSE.TXT')
        (self.project / 'GenericPopulation.csproj').write_text(
            '<Project><PropertyGroup><PackageLicenseExpression>MIT</PackageLicenseExpression></PropertyGroup></Project>')
        (self.cache / 'test.package.nuspec').write_text(
            '<package><metadata><license type="expression">MIT</license><copyright>Test author</copyright>'
            '<repository type="git" url="https://example.org/test" commit="abc"/></metadata></package>')
        self.package = {'id': 'Test.Package', 'version': '1.0.0', 'dependencyType': 'Direct',
                        'licenseExpression': 'MIT', 'contentHash': 'synthetic-hash', 'copyright': 'Test author',
                        'repository': {'type': 'git', 'url': 'https://example.org/test', 'commit': 'abc'},
                        'legalFiles': ['LICENSES/test.txt'], 'packagedLegalFiles': {'LICENSE.TXT': 'LICENSES/test.txt'}}
        self.inventory = {'schemaVersion': 1, 'targetFrameworks': {'net9.0': [self.package]},
                          'legalFiles': [{'path': 'LICENSES/test.txt',
                                          'sha256NormalizedUtf8': checker.text_hash(self.root / 'LICENSES/test.txt')}]}
        self.lock = {'dependencies': {'net9.0': {'Test.Package': {
            'type': 'Direct', 'resolved': '1.0.0', 'contentHash': 'synthetic-hash'}}}}
        self.write_json('LICENSES/inventory.json', self.inventory)
        self.write_json('src/GenericPopulation/packages.lock.json', self.lock)
        self.write_json('src/GenericPopulation/obj/project.assets.json', {
            'packageFolders': {str(self.root / 'cache'): {}},
            'libraries': {'Test.Package/1.0.0': {'type': 'package', 'sha512': 'synthetic-hash'}}})

    def write_json(self, relative, value):
        (self.root / relative).write_text(json.dumps(value), encoding='utf-8')

    def test_reviewed_package_passes_with_windows_line_endings(self):
        file = self.root / 'LICENSES/test.txt'
        file.write_bytes(b'\xef\xbb\xbf' + file.read_text(encoding='utf-8').replace('\n', '\r\n').encode('utf-8'))
        self.assertEqual(checker.verify(self.root), 1)

    def test_new_transitive_package_requires_review(self):
        self.lock['dependencies']['net9.0']['New.Package'] = {'type': 'Transitive', 'resolved': '1.0.0', 'contentHash': 'new'}
        self.write_json('src/GenericPopulation/packages.lock.json', self.lock)
        with self.assertRaisesRegex(ValueError, 'Pakkelisten'):
            checker.verify(self.root)

    def test_version_change_requires_review(self):
        self.lock['dependencies']['net9.0']['Test.Package']['resolved'] = '2.0.0'
        self.write_json('src/GenericPopulation/packages.lock.json', self.lock)
        with self.assertRaisesRegex(ValueError, 'version'):
            checker.verify(self.root)

    def test_changed_license_metadata_is_rejected(self):
        file = self.cache / 'test.package.nuspec'
        file.write_text(file.read_text().replace('>MIT<', '>GPL-3.0-only<'))
        with self.assertRaisesRegex(ValueError, 'lisensmetadata'):
            checker.verify(self.root)

    def test_truncated_notice_is_rejected(self):
        (self.root / 'LICENSES/test.txt').write_text('Synthetic copyright\n')
        with self.assertRaisesRegex(ValueError, 'Lisenstekst'):
            checker.verify(self.root)

    def test_missing_publish_notice_is_rejected(self):
        publish = self.root / 'publish'
        for relative in (*checker.DOCUMENTS, 'LICENSES/test.txt'):
            target = publish / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.root / relative, target)
        self.assertEqual(checker.verify(self.root, publish), 1)
        (publish / 'LICENSES/test.txt').unlink()
        with self.assertRaisesRegex(ValueError, 'Publisering mangler'):
            checker.verify(self.root, publish)


if __name__ == '__main__':
    unittest.main()
