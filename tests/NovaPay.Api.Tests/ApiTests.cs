using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaPay.Api;
using Xunit;

namespace NovaPay.Api.Tests;

public sealed class ApiTests
{
    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

    [Fact]
    public async Task WalletUsesIntegerKoboAndNairaCurrency()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        var wallet = await client.GetFromJsonAsync<Wallet>("/wallets/demo-wallet");
        Assert.NotNull(wallet);
        Assert.Equal(125_000L, wallet.BalanceKobo);
        Assert.Equal("NGN", wallet.Currency);
    }

    [Theory]
    [InlineData("/wallets/missing-wallet", HttpStatusCode.NotFound)]
    [InlineData("/wallets/invalid_id", HttpStatusCode.BadRequest)]
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/ready", HttpStatusCode.OK)]
    [InlineData("/version", HttpStatusCode.OK)]
    public async Task EndpointsReturnExpectedStatus(string path, HttpStatusCode expected)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task MetricsContainTemplatesNotWalletIds()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        await client.GetAsync("/wallets/private-wallet-id");
        var metrics = await client.GetStringAsync("/metrics");
        Assert.Contains("route=\"/wallets/{id}\",status=\"404\"} 1", metrics);
        Assert.DoesNotContain("private-wallet-id", metrics);
        Assert.DoesNotContain("route=\"/metrics\"", metrics);
    }

    [Fact]
    public async Task StoreFailureMakesReadinessUnavailableButLivenessHealthy()
    {
        await using var factory = Factory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWalletStore>();
            services.AddSingleton<IWalletStore, FailingStore>();
        }));
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var response = await client.GetAsync("/wallets/demo-wallet");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("sensitive-diagnostic", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void ProductionCannotStartWithDemoStore()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Production")
                .UseSetting("WalletStore:Provider", "Demo"));
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("demo store", exception.Message);
    }

    private sealed class FailingStore : IWalletStore
    {
        public Task<Wallet?> GetAsync(string id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive-diagnostic");

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("sensitive-diagnostic");
    }
}
