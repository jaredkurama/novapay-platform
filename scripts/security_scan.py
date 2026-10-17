"""Shared fail-closed scanner commands used by CI and security gate self-tests."""

import argparse
from pathlib import Path
import subprocess


def secret_command(tool, repository, report):
    return [str(tool), "git", str(repository), "--log-opts=--all", "--redact=100",
            "--exit-code=1", "--report-format=json", f"--report-path={report}"]


def vulnerability_command(tool, kind, target, report):
    command = [str(tool), kind, "--severity", "HIGH,CRITICAL", "--exit-code", "1",
               "--format", "json", "--output", str(report), "--timeout", "10m"]
    if kind in ("fs", "image"):
        command += ["--scanners", "vuln"]
    if kind == "fs":
        command += ["--skip-dirs", ".tools", "--skip-dirs", "artifacts",
                    "--skip-dirs", "**/bin", "--skip-dirs", "**/obj"]
    # Unfixed vulnerabilities are intentionally included. No ignore-unfixed/exit-code=0.
    return command + [str(target)]


def sast_command(dotnet, project):
    return [str(dotnet), "build", str(project), "--configuration", "Release",
            "--no-restore", "--no-incremental", "-p:EnableNETAnalyzers=true",
            "-p:AnalysisModeSecurity=All", "-p:RunAnalyzersDuringBuild=true",
            "-p:TreatWarningsAsErrors=true"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=["secrets", "dependencies", "image", "sast"])
    parser.add_argument("target")
    parser.add_argument("--tools", default=".tools/security")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--report", default="artifacts/security.json")
    args = parser.parse_args()
    Path(args.report).parent.mkdir(parents=True, exist_ok=True)
    if args.kind == "secrets":
        command = secret_command(Path(args.tools) / "gitleaks", args.target, args.report)
    elif args.kind == "sast":
        command = sast_command(args.dotnet, args.target)
    else:
        kind = "fs" if args.kind == "dependencies" else "image"
        command = vulnerability_command(Path(args.tools) / "trivy", kind, args.target, args.report)
    # Scanner errors and findings both fail the job. Reports alone do not determine success.
    return subprocess.run(command, check=False).returncode


if __name__ == "__main__":
    raise SystemExit(main())
