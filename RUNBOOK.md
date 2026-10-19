# Runbook: suspected unauthorized database access

Use this for the synthetic NovaPay AWS staging environment. The goal is to stop unauthorized access, preserve evidence, fix the cause, and restore service safely. No real FirstBank systems or customer data are involved.

## 1. Record the incident and pause changes

- Record an incident ID, discovery time in UTC, symptoms, and the original alert/log link.
- Assign an incident lead and an AWS/database operator. In a real incident, involve security and privacy owners through the approved contact list.
- Pause new GitHub deployments and Terraform changes. Check SSM commands already running: cancelling GitHub does not necessarily stop work on EC2.
- Use a trusted workstation and SSO identity. If either is suspected, use a separate approved response identity.
- Do not delete containers, logs, secrets, or snapshots. If harmful access is ongoing, contain it immediately and record any evidence you could not collect first.

## 2. Find the affected resources

| Resource | Current value |
|---|---|
| Account / region | `008971632699` / `eu-west-1` |
| CLI profile | `novapay` |
| EC2 instance / container | `i-0f497102e8d5a3051` / `novapay-wallet` |
| RDS instance / database | `novapay-staging` / `novapay` |
| Application DB user | `novapay_app` — wallet SELECT access |
| Application secret | `novapay-staging/database/application` |
| Application logs | `/novapay/staging/wallet` — 30-day retention |
| PostgreSQL logs | `/aws/rds/instance/novapay-staging/postgresql` — verify availability and retention |

Confirm identifiers if infrastructure has been recreated.

## 3. Collect evidence

These PowerShell commands are read-only. Confirm the expected account before continuing:

```powershell
aws sts get-caller-identity --profile novapay
aws logs tail /novapay/staging/wallet --since 2h --format detailed --profile novapay --region eu-west-1
aws ssm list-commands --instance-id i-0f497102e8d5a3051 --max-results 20 --profile novapay --region eu-west-1
aws secretsmanager list-secret-version-ids --secret-id novapay-staging/database/application --include-deprecated --profile novapay --region eu-west-1
aws rds describe-db-instances --db-instance-identifier novapay-staging --profile novapay --region eu-west-1 --query 'DBInstances[0].{Status:DBInstanceStatus,Groups:VpcSecurityGroups,Earliest:EarliestRestorableTime,Latest:LatestRestorableTime}'
```

Review CloudTrail for secret reads, SSM sessions/commands, role assumptions, and IAM, network or RDS changes during the incident window. Check GitHub workflow changes and runs too. Include other regions when relevant, including us-east-1 for global-service events.

Preserve original evidence in an approved encrypted location outside Git. Record source, collector, UTC time, and file hashes. Export all result pages; a terminal tail is only triage. Never copy passwords into chat, screenshots, tickets, or command history.

**Limits:** application logs do not show every database row read. PostgreSQL log export is enabled, but complete SQL-read auditing is not configured. CloudTrail records AWS API activity, not SQL. Missing logs do not prove no access occurred.

## 4. Identify and contain the access path

The incident lead approves disruptive actions and records expected downtime.

| Suspected cause | Action |
|---|---|
| Leaked application DB password | Disable login, end existing sessions, stop the API, then rotate the credential. |
| Compromised EC2 host | Preserve evidence, isolate the host and its AWS access, then rebuild. Do not retrieve replacement secrets through it. |
| Compromised GitHub deployment | Disable deployments, block new role assumptions, revoke applicable existing sessions, and investigate repository access. |
| Compromised administrator | Escalate account-level response and rotate affected privileged credentials. Changing only the app password is insufficient. |

An authorized DBA can inspect current sessions over a trusted private connection with TLS hostname verification:

```sql
SELECT pid, usename, application_name, client_addr, backend_start, state
FROM pg_stat_activity WHERE datname = 'novapay';
```

Review grants and memberships too. The app should have wallet SELECT access, no elevated role flags, and no inherited role memberships. A connection from EC2 is not automatically legitimate.

After the containment decision, the DBA can run this **disruptive** SQL:

```sql
ALTER ROLE novapay_app NOLOGIN;
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE usename = 'novapay_app' AND pid <> pg_backend_pid();
```

Confirm sessions ended. Disabling login or changing a password alone does not close existing connections. A separately configured DBA client and secure credential handling are prerequisites; the bootstrap tool is not a DBA console.

On a trusted EC2 host, stop repeated application connection attempts:

```shS
sudo docker update --restart=no novapay-wallet
sudo docker stop --time 20 novapay-wallet
```

Expect an outage. Network changes alone may not end existing connections. Coordinate session termination and isolation while retaining a trusted management path. Do not run Terraform apply blindly during containment: it could restore access deliberately removed.

## 5. Rotate credentials and remove the cause

**The bootstrap tool does not rotate existing passwords.** Automatic application-secret rotation is not configured. This procedure needs an authorized DBA and approved secret-safe tooling; that tooling is not included here.

1. Contain the original access path. Keep the API stopped and the application role disabled.
2. Generate a new random password in a trusted process. Store it as an `AWSPENDING` secret version, preserving the expected JSON fields. Do not use plaintext command arguments or SQL transcripts.
3. Update PostgreSQL to the same password. Review and remove unauthorized permissions after preserving evidence.
4. Restrict access to the trusted verification path, enable login, and test: TLS and wallet reads succeed, writes are denied, and the old password fails.
5. If verification fails, disable login again. Do not restore a compromised password just to recover availability.
6. Promote the verified secret version to `AWSCURRENT` and remove its `AWSPENDING` label. Record version IDs, not values.
7. Recreate the API with a trusted image and the new secret; running containers do not automatically reload credentials.
8. Retire old containers/password files after evidence preservation. Do not roll back to a container holding a revoked password. Redeploy an older trusted image with the new credential if needed.

For a compromised administrator password, use RDS-managed master-password rotation rather than editing that secret independently. Changing an IAM trust policy does not revoke already issued sessions; handle them separately.

## 6. Restore and verify

- Confirm the access path is closed and the host/deployment identities are trusted.
- If integrity is uncertain, restore a verified pre-incident point to a **new isolated RDS instance**. Keep the original for investigation; review restored credentials before use.
- Compare data with trusted records. In production, reconcile with the ledger and settlement records; restoring a database alone does not reconcile money.
- Deploy the reviewed image and current credential. Record the digest and SSM command ID.
- Through SSM forwarding, check `/health`, `/ready`, `/version`, and `/wallets/demo-wallet`. Confirm expected results and new CloudWatch events.
- Verify old credentials fail, unauthorized sessions are gone, and app writes remain denied.
- Reconcile emergency infrastructure changes deliberately. The incident lead approves restoration, monitoring ownership, and an observation period.

## 7. Communicate and close

Keep a timeline separating facts from assumptions. Do not claim no data was read when auditing is incomplete.

For real personal data, involve privacy/DPO and legal owners immediately. NDPA section 40(2) requires NDPC notification within 72 hours of awareness for a breach likely to risk individuals' rights and freedoms. Assess affected-person notification and any additional CBN, contractual, or card-industry duties with those owners. Do not wait for full root-cause analysis to escalate. This synthetic exercise does not itself require notifying regulators or customers.

Assign owners and dates to follow-up work: tested rotation tooling, approved DBA access, a restore drill, database audit coverage, durable CloudTrail collection, and delivered alerts. The local Prometheus example is not an AWS paging service.
