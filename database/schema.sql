-- Apply with a migration/owner identity, never the runtime application role.
CREATE TABLE IF NOT EXISTS public.wallets (
    wallet_id varchar(64) PRIMARY KEY CHECK (wallet_id ~ '^[A-Za-z0-9-]{1,64}$'),
    balance_kobo bigint NOT NULL CHECK (balance_kobo >= 0),
    currency varchar(3) NOT NULL CHECK (currency = 'NGN')
);
