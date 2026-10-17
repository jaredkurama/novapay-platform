using NovaPay.Api;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
// ASP.NET request scopes include raw paths, which can contain wallet identifiers.
builder.Logging.AddJsonConsole(options => options.IncludeScopes = false);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Services.AddSingleton<RequestMetrics>();
var provider = builder.Configuration["WalletStore:Provider"]
    ?? (builder.Environment.IsEnvironment("Testing") ? "Demo" : "Postgres");
if (provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
    {
        throw new InvalidOperationException("The demo store is restricted to Development/Testing.");
    }
    builder.Services.AddSingleton<IWalletStore, DemoWalletStore>();
}
else if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
{
    var settings = PostgresConfiguration.Read(builder.Configuration,
        builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"));
    builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(settings.ConnectionString));
    builder.Services.AddSingleton<IWalletStore, PostgresWalletStore>();
}
else
{
    throw new InvalidOperationException("WalletStore:Provider must be Postgres or Demo.");
}

var app = builder.Build();
app.UseRouting();
app.Use(async (context, next) =>
{
    // Route templates prevent wallet IDs and arbitrary URLs entering logs/metric labels.
    var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
    try
    {
        await next(context);
    }
    catch (Exception)
    {
        if (context.Response.HasStarted)
        {
            context.Abort();
            return;
        }
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "internal_error" });
    }
    finally
    {
        if (route != "/metrics")
        {
            context.RequestServices.GetRequiredService<RequestMetrics>().Record(route, context.Response.StatusCode);
            app.Logger.LogInformation("Request completed: {Route} {StatusCode} {TraceId}",
                route, context.Response.StatusCode, context.TraceIdentifier);
        }
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/ready", async (IWalletStore store, CancellationToken cancellationToken) =>
{
    try
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        return await store.IsReadyAsync(timeout.Token)
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "not_ready" }, statusCode: 503);
    }
    catch (Exception)
    {
        return Results.Json(new { status = "not_ready" }, statusCode: 503);
    }
});
app.MapGet("/version", () => Results.Ok(new
{
    service = "novapay-wallet",
    version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "local"
}));
app.MapGet("/wallets/{id}", async (string id, IWalletStore store, CancellationToken cancellationToken) =>
{
    if (id.Length > 64 || id.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
    {
        return Results.BadRequest(new { error = "invalid_wallet_id" });
    }
    var wallet = await store.GetAsync(id, cancellationToken);
    return wallet is null ? Results.NotFound() : Results.Ok(wallet);
});
app.MapGet("/metrics", (RequestMetrics metrics) => Results.Text(metrics.Render(), "text/plain; version=0.0.4"));
app.Run();

public partial class Program;
