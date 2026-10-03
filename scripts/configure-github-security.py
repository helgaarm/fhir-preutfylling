#!/usr/bin/env python3
"""Inspect or apply security settings for this repository through an authenticated gh CLI.

Read-only by default. --apply changes only helgaarm/fhir-preutfylling.
GitHub Administration permission is required for most writes.
"""
import argparse
import json
import subprocess
import sys

REPOSITORY = "helgaarm/fhir-preutfylling"
REQUIRED_CHECKS = [
    "Build and test", "Secret scan", "Workflow lint",
    "CodeQL (csharp)", "CodeQL (javascript-typescript)",
]


def api(path, method="GET", payload=None):
    target = f"repos/{REPOSITORY}" + (f"/{path}" if path else "")
    command = ["gh", "api", "--method", method, target]
    data = None
    if payload is not None:
        command += ["--input", "-"]
        data = json.dumps(payload)
    result = subprocess.run(command, input=data, text=True, capture_output=True, check=False)
    if result.returncode:
        # Print only the gh status line; never dump response bodies or credentials.
        reason = next((line for line in result.stderr.splitlines() if line.startswith("gh:")), "GitHub request failed")
        raise RuntimeError(reason)
    return json.loads(result.stdout) if result.stdout.strip() else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Apply settings to the repository")
    parser.add_argument("--require-review", action="store_true", help="Require one independent approval; needs a second maintainer")
    args = parser.parse_args()
    if args.require_review and not args.apply:
        parser.error("--require-review requires --apply")

    failures = []

    def attempt(name, action):
        try:
            details = action()
            print(f"OK: {name}" + (f" — {json.dumps(details)}" if details is not None else ""))
            return details
        except (RuntimeError, KeyError) as error:
            failures.append(name)
            print(f"BLOCKED: {name} — {error}")
            return None

    def inspect_settings():
        def inspect_secret_scanning():
            settings = api("").get("security_and_analysis")
            if settings is None:
                raise RuntimeError("GitHub did not expose secret-scanning settings to this credential")
            return {key: settings.get(key, "not exposed") for key in ("secret_scanning", "secret_scanning_push_protection")}
        attempt("Secret scanning and push protection", inspect_secret_scanning)
        attempt("Vulnerability alerts", lambda: api("vulnerability-alerts"))
        attempt("Dependabot security updates", lambda: api("automated-security-fixes"))
        attempt("Private vulnerability reporting", lambda: api("private-vulnerability-reporting"))
        attempt("Actions default permissions", lambda: api("actions/permissions/workflow"))
        attempt("Fork workflow approval", lambda: api("actions/permissions/fork-pr-contributor-approval"))
        attempt("Main branch protection", lambda: api("branches/main/protection"))

    if not args.apply:
        inspect_settings()
        return 1 if failures else 0

    attempt("Enable vulnerability alerts", lambda: api("vulnerability-alerts", "PUT"))
    attempt("Enable Dependabot security updates", lambda: api("automated-security-fixes", "PUT"))
    attempt("Enable private vulnerability reporting", lambda: api("private-vulnerability-reporting", "PUT"))
    attempt("Enable secret scanning and push protection", lambda: api("", "PATCH", {
        "security_and_analysis": {
            "secret_scanning": {"status": "enabled"},
            "secret_scanning_push_protection": {"status": "enabled"},
        },
        "delete_branch_on_merge": True,
    }) and {"requested": "secret scanning, push protection, delete merged branches"})
    attempt("Read-only default workflow token; no bot approvals", lambda: api("actions/permissions/workflow", "PUT", {
        "default_workflow_permissions": "read",
        "can_approve_pull_request_reviews": False,
    }))
    attempt("Require approval for all external fork contributors", lambda: api("actions/permissions/fork-pr-contributor-approval", "PUT", {
        "approval_policy": "all_external_contributors",
    }))

    def protect_main():
        runs = api("commits/main/check-runs?per_page=100")["check_runs"]
        # GitHub returns newest checks first. A stale success must not mask a newer failure.
        latest = {}
        for run in runs:
            latest.setdefault(run["name"], run)
        missing = [name for name in REQUIRED_CHECKS if latest.get(name, {}).get("conclusion") != "success"]
        if missing:
            raise RuntimeError("Wait for successful main checks before enforcing protection: " + ", ".join(missing))
        api("branches/main/protection", "PUT", {
            "required_status_checks": {"strict": True, "contexts": REQUIRED_CHECKS},
            "enforce_admins": True,
            "required_pull_request_reviews": {
                "dismiss_stale_reviews": True,
                "require_code_owner_reviews": args.require_review,
                "required_approving_review_count": 1 if args.require_review else 0,
                "require_last_push_approval": args.require_review,
            },
            "restrictions": None,
            "required_linear_history": True,
            "allow_force_pushes": False,
            "allow_deletions": False,
            "required_conversation_resolution": True,
        })
        return {"required_checks": REQUIRED_CHECKS, "independent_approvals": 1 if args.require_review else 0}

    attempt("Protect main after CI is green", protect_main)
    print("\nRe-run without --apply to inspect the effective settings.")
    return 1 if failures else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except FileNotFoundError:
        sys.exit("Install GitHub CLI (gh) and authenticate an account with repository administration access.")
