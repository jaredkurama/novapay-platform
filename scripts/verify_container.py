"""Verify a locally built image using Docker and Python 3 (no third-party packages)."""

import json
import atexit
import pathlib
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


def docker(*args):
    return subprocess.check_output(["docker", *args], text=True).strip()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


image = sys.argv[1] if len(sys.argv) > 1 else "novapay-wallet:local"
resources = []
temporary = tempfile.TemporaryDirectory(prefix="novapay-check-")


def cleanup():
    for resource in reversed(resources):
        subprocess.run(["docker", *resource], check=False, stdout=subprocess.DEVNULL)
    temporary.cleanup()


atexit.register(cleanup)
secret_dir = pathlib.Path(temporary.name)
for name in ("admin-password", "app-password"):
    path = secret_dir / name
    path.write_text(secrets.token_hex(32), encoding="utf-8")
    path.chmod(0o444)
network = docker("network", "create", "novapay-check-" + secrets.token_hex(6))
resources.append(("network", "rm", network))
database_dir = pathlib.Path(__file__).resolve().parent.parent / "database"
db = docker(
    "run", "--detach", "--network", network, "--network-alias", "postgres",
    "--env", "POSTGRES_DB=novapay", "--env", "POSTGRES_USER=novapay_admin",
    "--env", "POSTGRES_PASSWORD_FILE=/run/secrets/postgres_admin_password",
    "--mount", f"type=bind,src={secret_dir / 'admin-password'},dst=/run/secrets/postgres_admin_password,readonly",
    "--mount", f"type=bind,src={secret_dir / 'app-password'},dst=/run/secrets/postgres_app_password,readonly",
    "--mount", f"type=bind,src={database_dir / 'local-init.sh'},dst=/docker-entrypoint-initdb.d/001-init.sh,readonly",
    "--mount", f"type=bind,src={database_dir / 'schema.sql'},dst=/opt/novapay/schema.sql,readonly",
    "--mount", f"type=bind,src={database_dir / 'seed-demo.sql'},dst=/opt/novapay/seed-demo.sql,readonly",
    "postgres:17-alpine",
)
resources.append(("rm", "--force", "--volumes", db))


def wait_database():
    for attempt in range(60):
        result = subprocess.run(
            ["docker", "exec", db, "pg_isready", "-h", "127.0.0.1", "-U", "novapay_admin", "-d", "novapay"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        if result.returncode == 0:
            return
        time.sleep(1)
    raise RuntimeError("PostgreSQL did not become ready")


wait_database()
container = docker(
    "run", "--detach", "--read-only", "--cap-drop=ALL",
    "--security-opt=no-new-privileges:true",
    "--tmpfs", "/tmp:size=32m,mode=1777", "--memory=256m",
    "--cpus=0.5", "--pids-limit=128",
    "--publish", "127.0.0.1::8080",
    "--network", network,
    "--env", "ASPNETCORE_ENVIRONMENT=Development",
    "--env", "WalletStore__Provider=Postgres",
    "--env", "Database__Host=postgres", "--env", "Database__Name=novapay",
    "--env", "Database__Username=novapay_app", "--env", "Database__SslMode=Disable",
    "--env", "Database__PasswordFile=/run/secrets/postgres_app_password",
    "--mount", f"type=bind,src={secret_dir / 'app-password'},dst=/run/secrets/postgres_app_password,readonly",
    "--env", "APP_VERSION=container-verification", image,
)
resources.append(("rm", "--force", container))

try:
    details = json.loads(docker("inspect", container))[0]
    host = details["HostConfig"]
    require(details["Config"]["User"] == "1654:1654", "Unexpected configured user")
    require(host["ReadonlyRootfs"], "Root filesystem is writable")
    require("ALL" in host["CapDrop"], "Linux capabilities were not dropped")
    require("no-new-privileges:true" in host["SecurityOpt"], "Privilege escalation is not disabled")
    binding = details["NetworkSettings"]["Ports"]["8080/tcp"][0]
    require(binding["HostIp"] == "127.0.0.1", "Service is not restricted to loopback")
    base = "http://127.0.0.1:" + binding["HostPort"]
    # Ignore proxy environment settings for this explicitly local connection.
    http = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    def get(path):
        with http.open(base + path, timeout=6) as response:
            require(response.status == 200, "Unexpected HTTP status")
            return response.read().decode()

    for attempt in range(30):
        try:
            require(json.loads(get("/health"))["status"] == "healthy", "Liveness failed")
            break
        except (OSError, ValueError):
            if attempt == 29:
                raise
            time.sleep(1)

    require(json.loads(get("/ready"))["status"] == "ready", "Readiness failed")
    wallet = json.loads(get("/wallets/demo-wallet"))
    require(type(wallet["balanceKobo"]) is int and wallet["balanceKobo"] == 125000,
            "Wallet amount must be integer kobo")
    require(wallet["currency"] == "NGN", "Wallet currency must be NGN")
    require(json.loads(get("/version"))["version"] == "container-verification", "Version mismatch")
    metrics = get("/metrics")
    require('route="/wallets/{id}",status="200"} 1' in metrics, "Wallet metric missing")
    require("demo-wallet" not in metrics, "Wallet identifier leaked into metrics")

    docker("exec", db, "psql", "-U", "novapay_admin", "-d", "novapay", "-v", "ON_ERROR_STOP=1",
           "-c", "UPDATE public.wallets SET balance_kobo = 250000 WHERE wallet_id = 'demo-wallet'")
    require(json.loads(get("/wallets/demo-wallet"))["balanceKobo"] == 250000,
            "API did not read the updated PostgreSQL balance")
    denied = subprocess.run(
        ["docker", "exec", db, "psql", "-U", "novapay_admin", "-d", "novapay", "-v", "ON_ERROR_STOP=1",
         "-c", "SET ROLE novapay_app; UPDATE public.wallets SET balance_kobo = 0"],
        capture_output=True, text=True, check=False)
    require(denied.returncode != 0 and "permission denied" in denied.stderr,
            "Application role can modify wallets")
    docker("stop", db)
    for path, status, body in (("/ready", 503, {"status": "not_ready"}),
                               ("/wallets/demo-wallet", 500, {"error": "internal_error"})):
        try:
            get(path)
            raise RuntimeError("Database outage was not detected")
        except urllib.error.HTTPError as error:
            require(error.code == status and json.loads(error.read()) == body,
                    "Unexpected database outage response")
    require(json.loads(get("/health"))["status"] == "healthy", "Outage broke liveness")
    docker("start", db)
    wait_database()
    require(json.loads(get("/ready"))["status"] == "ready", "Readiness did not recover")
    require(json.loads(get("/wallets/demo-wallet"))["balanceKobo"] == 250000,
            "Balance did not persist across database restart")

    # The chiseled image has no shell/id executable. Inspect the real process via Docker.
    processes = docker("top", container, "-eo", "uid,pid,args").splitlines()
    require(len(processes) > 1, "No running application process")
    for process in processes[1:]:
        require(process.split()[0] == "1654", "Process is not running as UID 1654")

    logs = docker("logs", container)
    entries = [json.loads(line) for line in logs.splitlines() if line.strip()]
    require(any("Request completed" in entry.get("Message", "") for entry in entries),
            "Structured request logs missing")
    require("demo-wallet" not in logs, "Wallet identifier leaked into logs")
    print(json.dumps({
        "result": "passed",
        "image": image,
        "image_id": details["Image"],
        "process_uid": 1654,
        "read_only_rootfs": True,
        "capabilities_dropped": "ALL",
        "no_new_privileges": True,
        "binding": "loopback only",
        "checks": ["health", "readiness", "version", "integer kobo", "metrics", "JSON logs",
                   "PostgreSQL lookup", "read-only DB role", "database outage", "restart persistence"],
    }, indent=2))
finally:
    # Remove only the temporary container created by this script; preserve the image.
    cleanup()
    atexit.unregister(cleanup)
