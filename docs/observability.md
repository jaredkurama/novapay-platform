# Observability demonstration

The API emits structured JSON logs to stdout and exposes a Prometheus request counter at `/metrics`. Labels contain route templates and HTTP status codes, not wallet IDs. Logs and metrics must not contain passwords, BVNs/NINs, card details, or response bodies.

## What is live on staging

Docker sends the application's JSON logs to CloudWatch log group `/novapay/staging/wallet` in eu-west-1, with 30-day retention and separate streams for each release and candidate/active container. Actual event delivery has been verified. Request records include route template, status code, and trace ID. See [CloudWatch details](cloudwatch-logging.md) for permissions and logging limitations.

```powershell
aws logs tail /novapay/staging/wallet --since 15m --format short --profile novapay --region eu-west-1
```

| Endpoint | Purpose |
|---|---|
| `/health` | Confirms the application responds; does not check PostgreSQL. |
| `/ready` | Checks database access and permission to read the wallet table; returns 503 on failure. |
| `/metrics` | Exposes completed request counts by route template and HTTP status. |

The deployment script checks `/ready` and `/version` before accepting a release. These deployment-time checks are not continuous external monitoring. Staging endpoints remain private and can be accessed through SSM forwarding.

## Optional local monitoring

With Docker Compose installed, configure `NOVAPAY_SECRETS_DIR` as described by the local database setup, then run from the repository root:

```sh
docker compose -f compose.yaml -f compose.monitoring.yaml up -d --build
```

Open http://localhost:9090 and check the Targets and Alerts pages. Query `up{job="novapay-wallet"}` (expected 1), then call the local wallet endpoint and query `novapay_http_requests_total`. Prometheus scrapes the API over the Compose network every 15 seconds; its UI is bound only to host loopback. Its persistent volume has seven-day and 256 MB retention limits (whichever is reached first); the size limit is not a hard limit on total disk use.

```sh
curl http://localhost:8080/wallets/demo-wallet
docker compose -f compose.yaml -f compose.monitoring.yaml logs --tail 50 wallet
docker compose -f compose.yaml -f compose.monitoring.yaml down
```

Do not add `--volumes` when stopping unless intentionally deleting the local database and metric history.

## Alerts and tests

`NovaPayHighErrorRate` evaluates only wallet business requests for the `novapay-wallet` job. More than 5% must be server errors, with at least 20 requests in the rolling five-minute window, and that condition must hold for five minutes. Health/version traffic cannot dilute this ratio. Low-traffic failures can therefore remain below the alert's traffic threshold; this is a deliberate noise trade-off, not comprehensive outage detection.

`NovaPayScrapeFailed` fires after two minutes of failed scrapes. It does not detect a database outage while the API's metrics endpoint remains reachable, nor does it detect Prometheus itself stopping. Independent readiness probing and external monitoring remain production gaps.

CI runs the native Prometheus validator and rule tests:

```sh
docker run --rm --entrypoint /bin/promtool -v "$PWD/observability:/etc/prometheus:ro" prom/prometheus:v3.15.0 check config /etc/prometheus/prometheus.yml
docker run --rm --entrypoint /bin/promtool -v "$PWD/observability:/etc/prometheus:ro" -w /etc/prometheus prom/prometheus:v3.15.0 test rules alerts.test.yml
```

Tests exercise the hold duration, sustained errors, low-volume suppression, unrelated-service isolation, and scrape-failure recovery. They use synthetic time series and do not disrupt AWS or the database.

## Real versus demonstrated

The optional local Prometheus service evaluates rules and displays firing alerts. No Alertmanager or external notification receiver is configured, so these alerts do not send email or page an operator. This configuration is not installed on EC2.

Staging has centralized application logs in CloudWatch and working health/metrics endpoints. It does not yet have a persistent Prometheus collector, delivered notifications, independent readiness monitoring, or comprehensive database-read auditing. PostgreSQL log export is enabled, but neither that alone nor CloudTrail management-event history proves which database rows were read.

The non-blocking Docker log buffer can lose events during prolonged logging outages. Thirty-day retention is a staging choice, not a regulatory-compliance claim.

Prometheus configuration and rule tests passed, and a temporary direct-Docker smoke test confirmed live scraping and successful evaluation of both rules. Compose startup itself has not been verified in the development WSL environment, which lacked the Compose plugin.
