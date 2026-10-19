# Staging infrastructure deployment — 18 October 2026

Applied locally with the `novapay` AWS SSO profile in account `008971632699`, region `eu-west-1`, after refreshing and reviewing the saved plan.

Terraform completed successfully: **25 added, 0 changed, 0 destroyed**. The versioned staging state object is saved in S3 with AES256 encryption and native locking. No static AWS credentials were used.

## Resources

- EC2: `i-0f497102e8d5a3051`
- RDS: `novapay-staging`, PostgreSQL 17.9
- Database hostname: `novapay-staging.cdoemc24ah10.eu-west-1.rds.amazonaws.com`
- ECR: `008971632699.dkr.ecr.eu-west-1.amazonaws.com/novapay-wallet`

## Verification

EC2 is running and registered as Online in Systems Manager. SSM command `5d1aebb7-1853-4cad-ab15-2eda265d2634` completed successfully with exit code 0: cloud-init finished, Docker and amazon-ssm-agent are active, and Docker reports version 25.0.14.

RDS reports available with its parameter group in-sync. Private accessibility, storage encryption, seven-day backup retention, and deletion protection are configured. Creation took approximately ten minutes, including initialization/restarts and a completed initial backup.

SSM command `bc5a7b91-9de6-4111-a273-bec433d094a0` successfully opened TCP port 5432 from EC2 to the private database hostname. This verifies network connectivity only; it does not verify TLS certificate validation, database authentication, or application queries.

## Later updates (documentation reviewed 19 October 2026)

The 25-resource apply above is the historical foundation deployment. Since then, the schema, synthetic seed, read-only application role, and secret value were initialized and verified. Four resources were added for GitHub OIDC and deployment; the exact OIDC subject was subsequently corrected. GitHub deployment and live API checks succeeded.

CloudWatch logging added one log group and updated the runtime policy and SSM document. Redeployment command `faaf4e6f-9b9c-4454-8fcd-0c389f36bcbc` succeeded for version `0da615ab5a6d70fd45741617a26b27293df7016a`; actual JSON log delivery and 30-day retention were verified. These are dated observations, not claims about the latest running image.

Prometheus is an optional local setup. Persistent AWS metric collection, delivered notifications, automatic app-password rotation, and comprehensive SQL-read auditing remain gaps. Running infrastructure incurs AWS usage charges. See the [main README](../../README.md) and [runbook](../../RUNBOOK.md).

No infrastructure code was changed or committed during the original foundation apply. Local verification request files are under ignored `artifacts/` and contain no passwords.
