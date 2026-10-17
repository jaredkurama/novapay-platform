"""Install checksum-pinned Linux x64 CI tools without running remote install scripts."""

import argparse
import hashlib
import io
import json
from pathlib import Path
import platform
import tarfile
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("tools", nargs="+", choices=["gitleaks", "trivy", "actionlint"])
    parser.add_argument("--directory", default=".tools/security")
    args = parser.parse_args()
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
        parser.error("These pinned binaries require Linux x64. On Windows, run this in Ubuntu WSL.")
    manifest = json.loads(Path(__file__).with_name("security-tools.json").read_text())
    destination = Path(args.directory)
    destination.mkdir(parents=True, exist_ok=True)
    for name in args.tools:
        release = manifest[name]
        request = urllib.request.Request(release["url"], headers={"User-Agent": "novapay-ci"})
        print(f"Downloading {name} {release['version']}", flush=True)
        with urllib.request.urlopen(request, timeout=120) as response:
            archive = response.read()
        if hashlib.sha256(archive).hexdigest() != release["sha256"]:
            raise RuntimeError(f"Checksum mismatch for {name}; refusing to install")
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as bundle:
            # Copy just the regular binary, never extract paths from the remote archive.
            member = bundle.getmember(name)
            if not member.isfile():
                raise RuntimeError(f"Unexpected archive entry for {name}")
            with bundle.extractfile(member) as binary:
                target = destination / name
                target.write_bytes(binary.read())
        target.chmod(0o755)
        print(f"Installed verified {name} {release['version']}", flush=True)


if __name__ == "__main__":
    main()
