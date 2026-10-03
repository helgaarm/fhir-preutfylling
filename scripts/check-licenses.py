"""Kontroller lisensoversikten lokalt etter dotnet restore --locked-mode.

Ingen nettverkskall eller automatisk godkjenning av nye lisenser. Sammenligner alle
låste NuGet-pakker med gjennomgått metadata, originale merknader og eventuell publish-mappe.
Rettighetserklæringer og verktøyvilkår krever fortsatt manuell gjennomgang.
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DOCUMENTS = ('LICENSE', 'THIRD-PARTY-NOTICES.md', 'LICENSES/inventory.json',
             'docs/LICENSE_REVIEW.md', 'docs/PROVENANCE.md', 'examples/README.md')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def text_hash(path):
    """Tål Git-konvertering av linjeskift, men oppdag endret eller avkortet lisenstekst."""
    return hashlib.sha256(path.read_text(encoding='utf-8-sig').encode('utf-8')).hexdigest()


def repository_file(root, relative):
    path = (root / relative).resolve()
    require(path.is_relative_to(root.resolve()) and path.is_file(),
            f'Mangler fil eller ugyldig repo-sti: {relative}')
    return path


def verify(root, publish_dir=None):
    project = root / 'src/GenericPopulation'
    inventory = json.loads((root / 'LICENSES/inventory.json').read_text(encoding='utf-8'))
    lock = json.loads((project / 'packages.lock.json').read_text(encoding='utf-8'))
    assets = json.loads((project / 'obj/project.assets.json').read_text(encoding='utf-8-sig'))
    require(inventory['schemaVersion'] == 1, 'Ukjent lisensoversiktsformat.')
    require(set(inventory['targetFrameworks']) == set(lock['dependencies']),
            'Målrammeverk er endret. Gjennomgå og oppdater lisensoversikten.')
    project_xml = ET.parse(project / 'GenericPopulation.csproj').getroot()
    require(project_xml.findtext('./PropertyGroup/PackageLicenseExpression') == 'MIT',
            'Prosjektlisensen avviker fra gjennomgangen.')

    legal = {item['path']: item for item in inventory['legalFiles']}
    require(len(legal) == len(inventory['legalFiles']), 'Dupliserte lisensfiler i oversikten.')
    for relative, item in legal.items():
        require(text_hash(repository_file(root, relative)) == item['sha256NormalizedUtf8'],
                f'Lisenstekst er endret: {relative}')
    actual_legal = {p.relative_to(root).as_posix() for p in (root / 'LICENSES').glob('*.txt')}
    require(actual_legal == set(legal), 'Lisensfilene og oversikten dekker ikke samme sett filer.')

    count = 0
    expected_assets = set()
    for framework, packages in inventory['targetFrameworks'].items():
        by_id = {p['id']: p for p in packages}
        require(len(by_id) == len(packages), f'Dupliserte pakker for {framework}.')
        require(set(by_id) == set(lock['dependencies'][framework]),
                f'Pakkelisten er endret for {framework}. Krever lisensgjennomgang.')
        for name, package in by_id.items():
            locked = lock['dependencies'][framework][name]
            for key, lock_key in [('version', 'resolved'), ('dependencyType', 'type'), ('contentHash', 'contentHash')]:
                require(package[key] == locked[lock_key], f'{name}: {key} avviker fra låsefilen.')
            key = name + '/' + package['version']
            expected_assets.add(key.lower())
            restored = assets['libraries'].get(key)
            require(restored is not None and restored.get('sha512') == package['contentHash'],
                    f'{name}: restore stemmer ikke med låsefilen. Kjør restore --locked-mode.')
            cache = next((Path(folder) / name.lower() / package['version']
                          for folder in assets['packageFolders']
                          if (Path(folder) / name.lower() / package['version']).is_dir()), None)
            require(cache is not None, f'{name}: mangler i lokal NuGet-cache.')
            metadata = ET.parse(cache / (name.lower() + '.nuspec')).getroot()
            fields = {node.tag.split('}')[-1]: node for node in metadata.iter()}
            license_node = fields.get('license')
            require(license_node is not None and license_node.get('type') == 'expression'
                    and license_node.text == package['licenseExpression'],
                    f'{name}: lisensmetadata er endret eller mangler.')
            require(fields['copyright'].text == package['copyright']
                    and fields['repository'].attrib == package['repository'],
                    f'{name}: opphav/repository avviker fra gjennomgangen.')
            require(package['legalFiles'] and set(package['legalFiles']).issubset(legal),
                    f'{name}: mangler registrerte lisenstekster.')
            for original, relative in package['packagedLegalFiles'].items():
                require(relative in package['legalFiles'], f'{name}: ukjent kobling til lisenstekst.')
                require(text_hash(cache / original) == text_hash(root / relative),
                        f'{name}: kopiert {original} stemmer ikke med NuGet-pakken.')
            count += 1
    actual_assets = {k.lower() for k, v in assets['libraries'].items() if v['type'] == 'package'}
    require(actual_assets == expected_assets, 'Restore inneholder pakker utenfor lisensoversikten.')

    for relative in (*DOCUMENTS, *legal):
        source = repository_file(root, relative)
        if publish_dir is not None:
            target = publish_dir / relative
            require(target.is_file(), f'Publisering mangler {relative}')
            require(text_hash(source) == text_hash(target), f'Publisert fil er utdatert: {relative}')
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish-dir', type=Path, help='Kontroller også lisensfiler i publisert app.')
    args = parser.parse_args()
    try:
        count = verify(ROOT, args.publish_dir)
    except (ValueError, OSError, KeyError, ET.ParseError) as error:
        print(f'FAIL: {error}', file=sys.stderr)
        return 1
    print(f'PASS: {count} NuGet-pakker og tilhørende lisensfiler er kontrollert.'
          + (' Publisert dokumentasjon stemmer.' if args.publish_dir else ''))
    return 0


if __name__ == '__main__':
    sys.exit(main())
