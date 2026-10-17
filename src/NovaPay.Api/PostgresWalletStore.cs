using Npgsql;
using NpgsqlTypes;

namespace NovaPay.Api;

public sealed class PostgresWalletStore(NpgsqlDataSource dataSource) : IWalletStore
{
    public async Task<Wallet?> GetAsync(string id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "SELECT wallet_id, balance_kobo, currency FROM public.wallets WHERE wallet_id = $1");
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = id });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new Wallet(reader.GetString(0), reader.GetInt64(1), reader.GetString(2))
            : null;
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        // Verify the expected schema and SELECT permission, without reading customer rows.
        await using var command = dataSource.CreateCommand(
            "SELECT wallet_id, balance_kobo, currency FROM public.wallets LIMIT 0");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return reader.FieldCount == 3;
    }
}
