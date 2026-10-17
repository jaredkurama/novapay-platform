"""Verify a locally built image using Docker and Python 3 (no third-party packages)."""

import json
import subprocess
import sys
import time
import urllib.request


def docker(*args):
    return subprocess.check_output(["docker", *args], text=True).strip()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


image = sys.argv[1] if len(sys.argv) > 1 else "novapay-wallet:local"
container = docker(
    "run", "--detach", "--read-only", "--cap-drop=ALL",
    "--security-opt=no-new-privileges:true",
    "--tmpfs", "/tmp:size=32m,mode=1777", "--memory=256m",
    "--cpus=0.5", "--pids-limit=128",
    "--publish", "127.0.0.1::8080",
    "--env", "ASPNETCORE_ENVIRONMENT=Development",
    "--env", "APP_VERSION=container-verification", image,
)

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
        with http.open(base + path, timeout=2) as response:
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
        "checks": ["health", "readiness", "version", "integer kobo", "metrics", "JSON logs"],
    }, indent=2))
finally:
    # Remove only the temporary container created by this script; preserve the image.
    docker("rm", "--force", container)
