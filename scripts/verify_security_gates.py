"""Prove scanner gates fail using isolated, synthetic fixtures (never real secrets)."""

import argparse
import json
from pathlib import Path
import secrets
import subprocess
import tempfile

from security_scan import sast_command, secret_command, vulnerability_command


def checked(command, **kwargs):
    return subprocess.run(command, check=True, capture_output=True, text=True, **kwargs)


def test_secrets(tool, root):
    repository = root / "history"
    repository.mkdir()
    checked(["git", "init", "--quiet", str(repository)])
    fixture = repository / "synthetic.txt"
    # Construct a nonfunctional test identifier at runtime, never commit one to this repo.
    fake = "AK" + "IA" + secrets.token_hex(8).upper()
    fixture.write_text(f'aws_access_key_id = "{fake}"\n')
    checked(["git", "add", "synthetic.txt"], cwd=repository)
    identity = ["git", "-c", "user.name=Gate Test", "-c", "user.email=gate@example.invalid"]
    checked(identity + ["commit", "--quiet", "-m", "Synthetic fixture"], cwd=repository)
    fixture.unlink()
    checked(["git", "add", "-u"], cwd=repository)
    checked(identity + ["commit", "--quiet", "-m", "Remove fixture"], cwd=repository)
    report = root / "secret.json"
    result = subprocess.run(secret_command(tool, repository, report), capture_output=True, text=True)
    if result.returncode != 1 or not report.exists():
        raise RuntimeError("Secret gate did not reject a credential removed from current files")
    findings = json.loads(report.read_text())
    if not any(item.get("RuleID") == "aws-access-token" for item in findings):
        raise RuntimeError("Expected synthetic AWS identifier finding is missing")
    print("PASS: full-history secret gate rejects a removed synthetic credential")


def test_vulnerabilities(tool, root):
    # A package description, not an installed vulnerable library or executable.
    sbom = root / "known-vulnerable.cdx.json"
    sbom.write_text(json.dumps({
        "bomFormat": "CycloneDX", "specVersion": "1.5", "version": 1,
        "components": [{"type": "library", "group": "org.apache.logging.log4j",
                        "name": "log4j-core", "version": "2.14.1",
                        "purl": "pkg:maven/org.apache.logging.log4j/log4j-core@2.14.1"}],
    }))
    report = root / "vulnerabilities.json"
    result = subprocess.run(vulnerability_command(tool, "sbom", sbom, report), check=False)
    if result.returncode != 1 or not report.exists():
        raise RuntimeError("Vulnerability gate did not reject the known-vulnerable SBOM")
    findings = [finding for item in json.loads(report.read_text()).get("Results", [])
                for finding in item.get("Vulnerabilities", [])]
    if not any(finding["Severity"] == "CRITICAL" for finding in findings):
        raise RuntimeError("Expected critical-severity finding is missing")
    print("PASS: critical vulnerability gate returns a failing exit code")


def test_sast(dotnet, root):
    project = root / "GateTest.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                       '<TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>'
                       '</PropertyGroup></Project>')
    (root / "WeakCrypto.cs").write_text(
        "public static class WeakCrypto { public static byte[] Hash(byte[] data) { "
        "using var hash = System.Security.Cryptography.MD5.Create(); "
        "return hash.ComputeHash(data); } }")
    checked([dotnet, "restore", str(project)])
    result = subprocess.run(sast_command(dotnet, project), capture_output=True, text=True)
    if result.returncode == 0 or "error CA5351" not in result.stdout + result.stderr:
        raise RuntimeError("SAST gate did not reject insecure MD5 with CA5351")
    print("PASS: SAST rejects insecure cryptography as an error")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=["secrets", "vulnerabilities", "sast"])
    parser.add_argument("--tools", default=".tools/security")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="novapay-gate-") as directory:
        root = Path(directory)
        if args.kind == "secrets":
            test_secrets((Path(args.tools) / "gitleaks").resolve(), root)
        elif args.kind == "vulnerabilities":
            test_vulnerabilities((Path(args.tools) / "trivy").resolve(), root)
        else:
            test_sast(args.dotnet, root)


if __name__ == "__main__":
    main()
