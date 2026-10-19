# Staging foundation (eu-west-1)

This configuration manages the deployed AWS staging infrastructure and its SSM deployment document. Terraform provisions resources; GitHub Actions invokes the document to deploy the application. Last documentation review: 19 October 2026.

## Resources and access

- One VPC, one public application subnet, and two private database subnets.
- One x86 EC2 t3.micro, encrypted 12 GiB root disk, IMDSv2 required, Docker and SSM agent.
- No inbound EC2 security-group rules, SSH key, or public API listener. Access is through SSM port forwarding.
- Public IPv4 permits outbound HTTPS without a NAT gateway. This incurs IPv4 charges. Outbound TCP 443 is open to the internet because AWS APIs and image/package endpoints have changing addresses. PostgreSQL egress is restricted to the database security group.
- Single-AZ RDS PostgreSQL 17, db.t4g.micro, encrypted 20 GiB gp3 storage, seven-day backups, forced TLS, private endpoint, and application-group-only port 5432.
- Immutable ECR tags, scan-on-push, and retention of tagged releases for rollback. Untagged images expire after seven days.
- RDS manages its administrator password in Secrets Manager. Terraform creates the application secret resource; the separate initializer has populated its value and created the SELECT-only application role. Terraform does not read the passwords into state.
- The instance can pull the wallet image, retrieve the application secret, and write application log streams. It cannot read the administrator secret.
- GitHub OIDC trust matches this repository's immutable owner/repository IDs and master branch. The deployment role can publish the image and invoke the specific SSM document on this instance.
- CloudWatch application log group `/novapay/staging/wallet` retains events for 30 days. RDS PostgreSQL log export is also enabled, but does not constitute complete SQL-read auditing.
- Necessary IAM wildcards and their boundaries are explained in the [main README](../../README.md#why-some-iam-policies-contain-wildcards).

Two database subnets meet RDS subnet-group requirements; this does not enable Multi-AZ failover. Single-AZ is a staging cost tradeoff. These resources are not assumed to be free: check credit eligibility, EC2/RDS runtime, EBS/RDS storage, IPv4, backups, logs, ECR, and Secrets Manager before apply.

## Review locally

```powershell
$env:AWS_PROFILE = 'novapay'
aws sso login --profile novapay
aws sts get-caller-identity --profile novapay
$env:TF_VAR_aws_account_id = '008971632699'
terraform -chdir=infra/staging init
terraform -chdir=infra/staging fmt -check
terraform -chdir=infra/staging validate
terraform '-chdir=infra/staging' plan '-out=staging.tfplan'
```

The account guard rejects credentials from a different account. The provider uses the caller's normal AWS credential chain; no keys or profile names are embedded in Terraform.

Review the saved plan and cost estimate before applying it. Do not commit `.terraform`, state, `.tfvars`, or saved plans. Commit the provider lockfile. `backend.tf` configures encrypted S3 state with native lockfiles; the separate `infra/bootstrap` stack owns the versioned, private bucket. Backend credentials come from the AWS credential chain, not files committed to Git. Do not reuse a saved plan created before the backend migration.

## Deployment and remaining work

The database initializer, GitHub deployment, live endpoint checks, and CloudWatch delivery have been exercised. The deployed container uses verified database TLS, runtime secret mounts, non-root restrictions, and image digests. Candidate/readiness checks and rollback are implemented; failure-path tests use mocks.

For a new environment, provision infrastructure, initialize the database with the [operator tool](../../tools/NovaPay.DatabaseBootstrap/README.md), then deploy through the [workflow](../../.github/workflows/ci.yml). Update account/backend/repository/instance identifiers when adapting this project. The current configuration targets the existing assessment account.

Remaining gaps include automatic credential rotation, a restore drill, complete DB audit coverage, persistent AWS metrics collection, and delivered alerts. Local Prometheus configuration and tested alert rules are available. See the [threat model](../../THREAT_MODEL.md) and [incident runbook](../../RUNBOOK.md).

Deletion protection and final snapshots are enabled. Teardown needs a deliberate protection change and review of retained snapshots/secrets. A final snapshot with the configured name must not already exist. Keep state until cleanup is complete.
