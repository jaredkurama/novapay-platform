#!/bin/bash
set -euo pipefail
dnf install -y docker
install -d -m 0755 /etc/docker
cat > /etc/docker/daemon.json <<'JSON'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
JSON
systemctl enable --now docker
systemctl enable --now amazon-ssm-agent
# No application or database password is placed in user data.
# Image deployment and database bootstrap are separate, audited steps.
