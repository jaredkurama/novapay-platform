#!/bin/sh
# Local container bootstrap only; RDS roles/migrations will be managed separately.
set -eu
NOVAPAY_APP_PASSWORD=$(cat /run/secrets/postgres_app_password)
export NOVAPAY_APP_PASSWORD
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --set ON_ERROR_STOP=1 <<'SQL'
\getenv app_password NOVAPAY_APP_PASSWORD
CREATE ROLE novapay_app LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
SELECT format('ALTER ROLE novapay_app PASSWORD %L', :'app_password') \gexec
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
\i /opt/novapay/schema.sql
\i /opt/novapay/seed-demo.sql
GRANT CONNECT ON DATABASE novapay TO novapay_app;
GRANT USAGE ON SCHEMA public TO novapay_app;
GRANT SELECT ON public.wallets TO novapay_app;
SQL
unset NOVAPAY_APP_PASSWORD
