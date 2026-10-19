# Terraform state backend

This independent stack owns `novapay-tfstate-008971632699-eu-west-1` in `eu-west-1`. It is separate from the staging resources so staging teardown cannot remove the backend.

The bucket uses SSE-S3 (AES256) encryption, enabled versioning, BucketOwnerEnforced ownership (ACLs disabled), all four public-access blocks, and a policy denying non-TLS requests. No public or cross-account Allow policy is created. `prevent_destroy` and `force_destroy=false` protect against routine Terraform deletion; they do not prevent an AWS administrator from deleting it manually.

Both configurations use native S3 locking (`use_lockfile=true`); no DynamoDB table is required. The keys are:

- `bootstrap/terraform.tfstate`: backend resources, restricted to the bootstrap administrator.
- `staging/terraform.tfstate`: staging infrastructure.

Each key has a corresponding `.tflock` object while Terraform holds a lock. Versioning preserves previous state versions. Do not enable S3 Object Lock on these lockfiles or delete an active lock manually.

`novapay-staging-terraform-state` is a narrowly scoped managed IAM policy for a future Terraform operator role. It permits state reads/writes and lockfile reads/writes/deletes only for staging. It is intentionally unattached; the current SSO administrator already has access. This policy does not restrict existing administrators. Application deployment through GitHub does not itself require Terraform state access.

## Normal use

```powershell
$env:AWS_PROFILE = 'novapay'
terraform '-chdir=infra/bootstrap' init
terraform '-chdir=infra/bootstrap' plan

$env:TF_VAR_aws_account_id = '008971632699'
terraform '-chdir=infra/staging' init
terraform '-chdir=infra/staging' plan '-out=staging.tfplan'
```

The first bootstrap creation used local state because the bucket did not exist. After creation its state is migrated into the bootstrap key with `init -migrate-state`. A fresh checkout now initializes directly against the existing bucket. Do not repeat the initial local bootstrap or apply a saved plan produced before the backend migration.

For disaster recovery, restore the appropriate S3 object version while no Terraform operation is running, then inspect a fresh plan. If the bucket itself is deleted, recovery requires the protected local bootstrap backup or imports of surviving resources; versioning alone cannot recover a deleted bucket. Local state backups, cached backend configuration, and saved plans are Git-ignored and must be treated as sensitive.

S3 storage, requests, and retained versions incur usage charges. SSE-S3 avoids a separate customer-managed KMS key charge. State files are small, but costs are not assumed to be zero.

Reference: [HashiCorp S3 backend configuration and IAM permissions](https://developer.hashicorp.com/terraform/language/backend/s3).
