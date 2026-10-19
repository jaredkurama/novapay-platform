# RDS initialization evidence

Initialized `novapay` on the private `novapay-staging` RDS instance through an SSM tunnel. Local operator SSO credentials retrieved the administrator secret; the EC2 role was not modified.

Created the wallet schema and synthetic seed using `database/schema.sql` and `database/seed-demo.sql`. Created `novapay_app` with SELECT on `public.wallets` and schema USAGE. The role has no elevated role flags or inherited role memberships. Public schema creation was revoked from PUBLIC. No raw password was written to a local file, command argument, or tool output.

Live checks passed:

- TLS certificate chain and real RDS hostname verification, despite TCP forwarding through localhost.
- A PostgreSQL connection reporting SSL active.
- Reading the seeded wallet as the application account.
- UPDATE and public-schema CREATE denied with SQLSTATE 42501.
- A deliberately incorrect TLS hostname rejected.
- A plaintext connection rejected by RDS.
- A second bootstrap invocation verified existing credentials and exited without database changes or password rotation.

The first initialization transaction rolled back on a role-management permission error. Secret-stage retry handling was corrected after observing that the first secret version also had AWSCURRENT. The successful attempt reused the pending credential and finalized it only after validation. The application secret is `novapay-staging/database/application`; its JSON contains engine, host, port, dbname, username, and password. Do not copy its value into this repository.

The wallet API has since been deployed through GitHub OIDC, ECR, and SSM. Runtime secret retrieval, RDS CA mounting, and readiness checks have been exercised successfully. Rollback behavior is covered by mocked tests; a live failure/recovery drill is not claimed. See [deployment details](deployment-workflow.md).

This is historical initialization evidence, not a fresh database audit. The initializer verifies existing finalized credentials without rotating them. Subsequent deployment writes the application password into private host release directories for read-only container mounts; the statement above about avoiding local password files describes the initialization tool, not all runtime secret storage.
