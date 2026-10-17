using Npgsql;

namespace NovaPay.Api;

public static class PostgresConfiguration
{
    public static NpgsqlConnectionStringBuilder Read(IConfiguration configuration, bool allowLocalSettings)
    {
        string Required(string key) => !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"Missing required configuration: {key}.");

        var sslModeText = configuration["Database:SslMode"] ?? "VerifyFull";
        if (!Enum.TryParse<SslMode>(sslModeText, true, out var sslMode) || !Enum.IsDefined(sslMode))
        {
            throw new InvalidOperationException("Database:SslMode is invalid.");
        }
        if (!allowLocalSettings && sslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException("PostgreSQL requires VerifyFull TLS outside Development/Testing.");
        }

        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = Required("Database:Host"),
            Port = configuration.GetValue("Database:Port", 5432),
            Database = Required("Database:Name"),
            Username = Required("Database:Username"),
            SslMode = sslMode,
            // Use password authentication and TLS; the chiseled image has no Kerberos libraries.
            GssEncryptionMode = GssEncryptionMode.Disable,
            Timeout = 3,
            CommandTimeout = 3,
            MaxPoolSize = 10,
            IncludeErrorDetail = false,
            ApplicationName = "novapay-wallet"
        };
        if (!string.IsNullOrWhiteSpace(configuration["Database:RootCertificate"]))
        {
            settings.RootCertificate = configuration["Database:RootCertificate"];
        }

        var password = configuration["Database:Password"];
        var passwordFile = configuration["Database:PasswordFile"];
        if (!string.IsNullOrWhiteSpace(passwordFile))
        {
            if (!string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("Use Database:PasswordFile or Database:Password, not both.");
            }
            try
            {
                password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Do not include secret contents or an inner exception in startup diagnostics.
                throw new InvalidOperationException("Unable to read the PostgreSQL password file.");
            }
        }
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("Provide Database:PasswordFile or Database:Password at runtime.");
        }
        settings.Password = password;
        return settings;
    }
}
