# Initialize staging PostgreSQL

This operator tool runs locally under an AWS SSO identity. It uses Npgsql to connect through an existing SSM tunnel and the AWS Secrets Manager SDK to read/write credentials in memory. It never gives the EC2 instance role access to the administrator secret.

Run from the repository root. Download the official RDS CA bundle to an ignored local directory:

```powershell
New-Item -ItemType Directory -Force .tools/rds | Out-Null
Invoke-WebRequest https://truststore.pki.rds.amazonaws.com/eu-west-1/eu-west-1-bundle.pem -OutFile .tools/rds/eu-west-1-bundle.pem
```

Open an SSM `AWS-StartPortForwardingSessionToRemoteHost` session through the staging EC2 instance to the RDS hostname, remote port 5432, local port **15432**. The workstation needs the AWS CLI, Session Manager plugin, and .NET 10 SDK. Its SSO identity needs SSM session permissions plus GetSecretValue on the administrator/application secrets and PutSecretValue/UpdateSecretVersionStage on the application secret. The runtime EC2 role remains unchanged.

```powershell
dotnet run --project tools/NovaPay.DatabaseBootstrap --configuration Release -- `
  novapay eu-west-1 '<RDS hostname>' '<administrator secret ARN>' `
  '<application secret ARN>' .tools/rds/eu-west-1-bundle.pem
```

Arguments are identifiers only. Passwords and temporary AWS credentials must never be supplied on the command line. The tool obtains temporary AWS credentials from the CLI credential process and does not print its output. Exceptions report only the operation stage, exception class, or PostgreSQL SQLSTATE/routine.

## Behavior

1. If a finalized current application credential exists, verify it and exit without changes.
2. Otherwise, connect as administrator using TLS VerifyFull and the official RDS CA. The TCP destination is localhost through SSM, while the TLS target hostname remains the actual RDS endpoint. No custom certificate-acceptance callback is installed.
3. Generate a random password once and save it as an AWSPENDING version, or reuse an existing pending version after a failed attempt. Secrets Manager can also assign AWSCURRENT to its first version; **AWSPENDING marks incomplete initialization**, and deployment must wait for that marker to be removed. A different pending/current version pair requires manual review rather than implicit rotation.
4. In a database transaction, create the restricted role, apply the checked-in schema and synthetic seed, and grant SELECT on wallets. Existing seed balances are not overwritten. An existing role without a managed pending credential is rejected. Elevated role flags or memberships are rejected.
5. Supply PostgreSQL a SCRAM-SHA-256 verifier rather than a raw password in SQL. Password derivation uses a random salt and 100,000 iterations. SQL migration text is limited to fixed statements and the two reviewed repository SQL files; the single CA2100 suppression is scoped to that migration execution call.
6. Verify a real application login, encrypted connection, wallet read, denied UPDATE and CREATE, rejected incorrect TLS hostname, and rejected plaintext connection. Permission probes roll back and do not modify wallet balances.
7. Finalize AWSCURRENT and remove AWSPENDING only after successful verification. Database and Secrets Manager cannot share a transaction: if publication fails after the database commit, rerun using the saved pending credential.

The first RDS attempt rejected explicit modification of privileged role attributes. The tool now relies on PostgreSQL's restricted defaults, checks that elevated attributes are absent, and changes only LOGIN/password. No additional administrator or application privileges were granted.

This is a one-time staging initializer and verifier, not a general schema-migration or password-rotation service. Close the SSM session when finished. The API image does not contain this tool or its AWS SDK dependencies.
