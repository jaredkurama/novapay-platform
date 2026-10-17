-- Synthetic demonstration data only. Never overwrite an existing balance.
INSERT INTO public.wallets (wallet_id, balance_kobo, currency)
VALUES ('demo-wallet', 125000, 'NGN')
ON CONFLICT (wallet_id) DO NOTHING;
