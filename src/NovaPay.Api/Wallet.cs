namespace NovaPay.Api;

public sealed record Wallet(string Id, long BalanceKobo, string Currency);

public interface IWalletStore
{
    Task<Wallet?> GetAsync(string id, CancellationToken cancellationToken);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

// Synthetic data only; the deployed service will use a separate datastore adapter.
public sealed class DemoWalletStore : IWalletStore
{
    public Task<Wallet?> GetAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(id == "demo-wallet" ? new Wallet(id, 125_000, "NGN") : null);

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
