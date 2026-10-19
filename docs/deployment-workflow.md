# Staging deployment

The existing CI workflow now publishes and deploys on pushes to `master`, or manual runs targeting `master`. Pull requests run checks without deployment. AWS resources are managed by `infra/staging/deployment.tf`; GitHub does not run Terraform.

## Pipeline sequence

1. Restore locked dependencies, check formatting, run tests and C# security analysis; validate Prometheus configuration/rules and scan Git history and dependencies. Quality and security jobs run in parallel, and both must pass.
2. Build the multi-stage container once, scan it with Trivy, and run PostgreSQL/container integration checks.
3. Run the deployment script's mocked failure-path tests.
4. Obtain temporary AWS credentials using GitHub OIDC and log in to ECR with the official AWS actions. No static AWS access keys are needed in GitHub secrets.
5. Push the same locally scanned image with a unique commit/run/attempt tag. Resolve its digest from ECR and check it against Docker's repository digest.
6. Send the digest and commit SHA to the `novapay-staging-deploy` SSM document on the staging instance.
7. Wait for SSM completion. A failed deployment fails CI even when rollback succeeds. Upload deployment output with the container evidence.

The role trust requires `repo:jaredkurama@137181595/novapay-platform@1423430941:ref:refs/heads/master` and audience `sts.amazonaws.com`. Its policy permits publishing to the wallet repository, invoking this document on this instance, and reading command results. It cannot read database secrets or change Terraform infrastructure. The EC2 instance role retrieves the application secret. Protecting changes to `master` and the workflow is therefore important: code merged there can deploy to staging.

## On the EC2 instance

The SSM document renders `deploy-staging.sh.tftpl`. Inputs have strict digest/SHA patterns and arrive as environment variables. The script locks deployments, fetches the initialized application secret, downloads the RDS CA bundle, and pulls the image by digest.

A candidate container starts on an automatically allocated loopback port. Both `/ready` (including database access) and `/version` must pass. The candidate is then removed, the current container is retained as `novapay-previous`, and the new container starts on `127.0.0.1:8080`. A failed final check restores the previous container and verifies it. A first deployment has no previous release to restore. This single-host approach has brief downtime during the switch; it is not a zero-downtime load-balanced deployment.

Containers run as UID 1654, with a read-only root filesystem, dropped capabilities, no new privileges, and memory/CPU/process limits. The password and CA are mounted as read-only files. Release directories are accessible only to root on encrypted EBS and survive host restarts; the active and previous release retain their mounted files. Temporary registry credentials and raw secret responses are deleted. This is not automatic database password rotation.

The API has no public listener. Use an SSM port-forwarding session to instance port 8080 for endpoint checks. No SSH ingress or GitHub runner IP allowlist is required.

Docker sends candidate and active application logs to CloudWatch using the EC2 role. Streams include the commit and release identifier; retention is 30 days. See [logging details](cloudwatch-logging.md). A rollback restores the old container's original logging and credential configuration, so do not use it to revive a revoked credential after an incident.

## Verification status

Local checks cover successful first deployment, successful upgrade, failed candidate, failed replacement with rollback, failed first deployment, and refusal of an unfinished secret. These tests fake AWS, Docker, and HTTP; they are not a live recovery drill.

The initial GitHub run failed because the trust policy used the older name-only OIDC subject. CloudTrail showed the immutable IDs; commit `0da615a` corrected the exact match, and a passing GitHub deployment was subsequently reported. Live endpoint checks through SSM also passed. The same running image was later redeployed through SSM document version 2 to enable CloudWatch; readiness and actual JSON delivery were verified.

Commit `4d2cf3d` added monitoring tests and logging configuration to the repository and was pushed. Its particular GitHub run result has not been independently checked in this documentation review. Consult Actions for the latest run rather than treating historical evidence as current status.

Do not cancel an active deployment unnecessarily. Master runs do not automatically cancel earlier runs, and the host also serializes deployment scripts. If the workflow times out, inspect the SSM command status before retrying; a runner timeout does not prove the host stopped work.
