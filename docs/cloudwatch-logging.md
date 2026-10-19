# Staging application logs

Terraform creates `/novapay/staging/wallet` in eu-west-1 with 30-day retention. This is a cost-conscious synthetic staging policy, not a regulatory retention determination. CloudWatch encrypts logs at rest by default; this deployment does not add a customer-managed KMS key or immutable archive. Log ingestion and storage may incur AWS charges.

The configuration has been applied. An SSM redeployment of the existing application image passed readiness checks, and actual startup/request JSON events plus the retention setting were verified. This is delivery evidence, not a fresh health check on every documentation read.

The Docker `awslogs` driver sends the API's JSON stdout/stderr using the host instance role. The application receives neither AWS credentials nor CloudWatch permissions. Each stream includes the commit SHA, unique release directory name, and candidate/active container name. The instance can only create streams and write events in this group; the `:log-stream:*` wildcard is required for dynamically named release streams. Terraform owns group creation and retention. IAM deletion/read permissions are not granted to the instance.

Docker uses non-blocking logging with a 4 MB buffer. If the destination stays unavailable and the buffer fills, logs may be lost; initial logging-driver setup can also prevent container startup. Existing candidate/readiness/rollback logic handles startup failure. Local Docker log caching is bounded to three 10 MB files per container. Already delivered CloudWatch events survive container removal until retention expiry; old container logs are not backfilled.

Read recent events with your authorized operator SSO profile:

```powershell
aws logs tail /novapay/staging/wallet --since 15m --format short --profile novapay --region eu-west-1
```

Use the stream's commit/release identifier to distinguish candidate checks from active requests. Application records contain route templates, HTTP status codes, and trace IDs. Do not introduce request bodies, raw wallet IDs, passwords, BVNs/NINs, or card data into these logs. Future log changes require review; the driver does not automatically redact content.

This collection covers application events, not PostgreSQL statement/access auditing, SSM session recordings, or a durable CloudTrail trail. It does not create metrics collection or notification delivery. Logs remain in Ireland; include them in the data-residency assessment. A rollback to a pre-logging container restores its old logging configuration, so verify the driver after rollback. An already running container only adopts the new driver when recreated.
