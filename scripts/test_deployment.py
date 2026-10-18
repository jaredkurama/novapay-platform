"""Exercise deployment/rollback using fake AWS, Docker and HTTP commands; no cloud access."""

import json
import os
from pathlib import Path
import shlex
import subprocess
import sys
import tempfile


def mock(command, args):
    path = Path(os.environ["DEPLOY_TEST_STATE"])
    state = json.loads(path.read_text())
    containers = state["containers"]
    result = 0
    if command == "aws":
        if args[0] == "secretsmanager":
            stages = ["AWSCURRENT"] + (["AWSPENDING"] if state["mode"] == "pending-secret" else [])
            print(json.dumps({"VersionStages": stages, "SecretString": json.dumps({
                "host": "database.example", "port": 5432, "dbname": "novapay",
                "username": "novapay_app", "engine": "postgres", "password": "synthetic-test-value"
            })}))
        else:
            print("synthetic-registry-token")
    elif command == "curl":
        if "-o" in args:
            Path(args[args.index("-o") + 1]).write_text("synthetic-certificate")
        else:
            url = args[-1]
            port = url.split(":")[-1].split("/")[0]
            container = next((c for c in containers.values() if c["running"] and c["port"] == port), None)
            failed = (state["mode"] == "candidate-failure" and port == "18080") or (
                state["mode"] == "replacement-failure" and port == "8080" and container and container["version"] == "a" * 40)
            if container is None or failed:
                result = 22
            else:
                print(json.dumps({"status": "ready"} if url.endswith("/ready") else {"version": container["version"]}))
    elif command == "docker":
        operation = args[0]
        name = args[-1]
        if operation == "inspect":
            if name not in containers:
                return 1
            container = containers[name]
            pattern = args[args.index("--format") + 1] if "--format" in args else ""
            if "novapay.managed" in pattern:
                print("true")
            elif "novapay.release" in pattern:
                print(container["release"])
            elif "Config.Env" in pattern:
                print(json.dumps(["APP_VERSION=" + container["version"]]))
            elif "HostPort" in pattern:
                print(container["port"])
            else:
                print("{}")
        elif operation == "run":
            name = args[args.index("--name") + 1]
            env = [args[i + 1] for i, arg in enumerate(args) if arg == "--env"]
            labels = [args[i + 1] for i, arg in enumerate(args) if arg == "--label"]
            port = args[args.index("--publish") + 1].split(":")[1] or "18080"
            if name in containers or any(c["running"] and c["port"] == port for c in containers.values()):
                return 125
            containers[name] = {
                "version": next(e.split("=", 1)[1] for e in env if e.startswith("APP_VERSION=")),
                "release": next(e.split("=", 1)[1] for e in labels if e.startswith("novapay.release=")),
                "port": port, "running": True
            }
        elif operation == "rm":
            containers.pop(name, None)
        elif operation == "rename":
            containers[args[2]] = containers.pop(args[1])
        elif operation in ("start", "stop"):
            containers[name]["running"] = operation == "start"
        elif operation == "login":
            sys.stdin.read()
        elif operation not in ("pull", "update"):
            raise AssertionError("Unexpected Docker operation: " + operation)
    else:
        raise AssertionError("Unexpected mock command")
    path.write_text(json.dumps(state))
    return result


def run_case(mode, existing):
    root = Path(__file__).resolve().parent.parent
    with tempfile.TemporaryDirectory(prefix="novapay-deploy-test-") as directory:
        directory = Path(directory)
        releases = directory / "releases"
        releases.mkdir()
        containers = {}
        if existing:
            old = releases / "release.old"
            old.mkdir()
            (old / "password").write_text("synthetic-old-value")
            containers["novapay-wallet"] = {"version": "b" * 40, "release": str(old), "port": "8080", "running": True}
        state_path = directory / "state.json"
        state_path.write_text(json.dumps({"mode": mode, "containers": containers}))
        bin_dir = directory / "bin"
        bin_dir.mkdir()
        for command in ("aws", "docker", "curl"):
            executable = bin_dir / command
            executable.write_text("#!/bin/sh\nexec python3 " + shlex.quote(str(Path(__file__).resolve())) + " --mock " + command + ' "$@"\n')
            executable.chmod(0o755)
        sleep = bin_dir / "sleep"
        sleep.write_text("#!/bin/sh\nexit 0\n")
        sleep.chmod(0o755)
        script = (root / "infra/staging/deploy-staging.sh.tftpl").read_text()
        for key, value in {"repository": "registry.example/novapay-wallet", "secret_arn": "synthetic-secret-id", "db_host": "database.example"}.items():
            script = script.replace("${" + key + "}", value)
        script = script.replace("/var/lib/novapay/releases", str(releases)).replace("/run/lock/novapay-deploy.lock", str(directory / "deploy.lock"))
        script_path = directory / "deploy.sh"
        script_path.write_text(script)
        env = dict(os.environ, PATH=str(bin_dir) + os.pathsep + os.environ["PATH"],
                   DEPLOY_TEST_STATE=str(state_path), SSM_ImageDigest="sha256:" + "c" * 64, SSM_AppVersion="a" * 40)
        result = subprocess.run(["bash", str(script_path)], env=env, capture_output=True, text=True, timeout=30)
        containers = json.loads(state_path.read_text())["containers"]
        assert "novapay-candidate" not in containers, result.stderr
        if mode == "success":
            assert result.returncode == 0, result.stdout + result.stderr
            assert containers["novapay-wallet"]["version"] == "a" * 40
            assert Path(containers["novapay-wallet"]["release"], "password").exists()
            if existing:
                assert containers["novapay-previous"]["version"] == "b" * 40
                assert not containers["novapay-previous"]["running"]
        else:
            assert result.returncode != 0
            if existing:
                assert containers["novapay-wallet"]["version"] == "b" * 40
                assert containers["novapay-wallet"]["running"]
                assert Path(containers["novapay-wallet"]["release"], "password").exists()
            else:
                assert "novapay-wallet" not in containers
            assert len(list(releases.iterdir())) == int(existing), result.stdout + result.stderr
        assert not list(releases.glob("*/docker-auth")), "Registry credentials retained"
        assert not list(releases.glob("*/secret.json")), "Raw secret response retained"
        print(f"Passed: {mode}, existing release={existing}")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--mock":
        sys.exit(mock(sys.argv[2], sys.argv[3:]))
    for mode, existing in [("success", False), ("success", True), ("candidate-failure", True),
                           ("replacement-failure", True), ("replacement-failure", False), ("pending-secret", True)]:
        run_case(mode, existing)
