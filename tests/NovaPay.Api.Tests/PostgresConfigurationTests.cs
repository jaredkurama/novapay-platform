using Microsoft.Extensions.Configuration;
using NovaPay.Api;
using Npgsql;
using Xunit;

namespace NovaPay.Api.Tests;

public sealed class PostgresConfigurationTests
{
    private static Dictionary<string, string?> Settings() => new()
    {
        ["Database:Host"] = "localhost",
        ["Database:Name"] = "novapay",
        ["Database:Username"] = "novapay_app",
        ["Database:Password"] = Guid.NewGuid().ToString("N")
    };

    [Fact]
    public void DefaultsToCertificateAndHostnameVerifiedTls()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build();
        var settings = PostgresConfiguration.Read(config, false);
        Assert.Equal(SslMode.VerifyFull, settings.SslMode);
        Assert.False(settings.IncludeErrorDetail);
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    public void ProductionRejectsUnverifiedConnections(string sslMode)
    {
        var settings = Settings();
        settings["Database:SslMode"] = sslMode;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.Throws<InvalidOperationException>(() => PostgresConfiguration.Read(config, false));
    }

    [Fact]
    public void LocalDevelopmentCanUseNonTlsDatabase()
    {
        var settings = Settings();
        settings["Database:SslMode"] = "Disable";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.Equal(SslMode.Disable, PostgresConfiguration.Read(config, true).SslMode);
    }

    [Fact]
    public void MissingPasswordFailsWithoutExposingConfiguration()
    {
        var settings = Settings();
        settings.Remove("Database:Password");
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var exception = Assert.Throws<InvalidOperationException>(() => PostgresConfiguration.Read(config, true));
        Assert.Contains("at runtime", exception.Message);
        Assert.DoesNotContain("novapay_app", exception.Message);
    }

    [Fact]
    public void PasswordFileIsReadAndCompetingSourcesAreRejected()
    {
        var passwordFile = Path.GetTempFileName();
        try
        {
            var password = Guid.NewGuid().ToString("N");
            File.WriteAllText(passwordFile, password + "\n");
            var settings = Settings();
            settings.Remove("Database:Password");
            settings["Database:PasswordFile"] = passwordFile;
            var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            Assert.Equal(password, PostgresConfiguration.Read(config, true).Password);

            settings["Database:Password"] = Guid.NewGuid().ToString("N");
            config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            Assert.Throws<InvalidOperationException>(() => PostgresConfiguration.Read(config, true));
        }
        finally
        {
            File.Delete(passwordFile);
        }
    }
}
