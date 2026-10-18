using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Npgsql;

// Run from the repository root through an existing SSM port-forwarding session.
// Arguments contain identifiers and certificate paths only, never passwords.
if (args.Length != 6)
{
    Console.Error.WriteLine("Usage: <profile> <region> <RDS hostname> <admin secret ARN> <application secret ARN> <CA bundle path>");
    return 1;
}
var stage = "configuration";
try
{
    var (profile, region, host, adminSecret, appSecret, caPath) = (args[0], args[1], args[2], args[3], args[4], args[5]);
    if (!File.Exists(caPath) || !File.Exists("database/schema.sql"))
        throw new InvalidOperationException("Run from the repository root with an existing CA bundle.");

    stage = "temporary AWS credentials";
    var processInfo = new ProcessStartInfo("aws")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    foreach (var argument in new[] { "configure", "export-credentials", "--profile", profile, "--format", "process" })
        processInfo.ArgumentList.Add(argument);
    using var process = Process.Start(processInfo) ?? throw new InvalidOperationException();
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    await stderr; // Never print credential-process output, including diagnostics.
    if (process.ExitCode != 0)
        throw new InvalidOperationException("AWS credential export failed.");
    using var exported = JsonDocument.Parse(await stdout);
    var aws = exported.RootElement;
    using var secrets = new AmazonSecretsManagerClient(new SessionAWSCredentials(
        aws.GetProperty("AccessKeyId").GetString(), aws.GetProperty("SecretAccessKey").GetString(),
        aws.GetProperty("SessionToken").GetString()), RegionEndpoint.GetBySystemName(region));

    async Task<GetSecretValueResponse?> ReadSecret(string id, string versionStage)
    {
        try
        {
            return await secrets.GetSecretValueAsync(new GetSecretValueRequest { SecretId = id, VersionStage = versionStage });
        }
        catch (ResourceNotFoundException)
        {
            return null;
        }
    }

    NpgsqlDataSource ConnectAs(string username, string password, string? certificateHostname = null, bool tls = true)
    {
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 15432,
            Database = "novapay",
            Username = username,
            Password = password,
            SslMode = tls ? SslMode.VerifyFull : SslMode.Disable,
            GssEncryptionMode = GssEncryptionMode.Disable,
            RootCertificate = tls ? Path.GetFullPath(caPath) : null,
            Pooling = false,
            Timeout = 10,
            CommandTimeout = 15,
            IncludeErrorDetail = false,
            ApplicationName = "novapay-database-bootstrap"
        };
        var source = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        // TCP reaches localhost through SSM, but TLS must verify the real RDS hostname.
        // Keep Npgsql's certificate validation enabled; override only the TLS target name.
        source.UseSslClientAuthenticationOptionsCallback(options => options.TargetHost = certificateHostname ?? host);
        return source.Build();
    }

    async Task Verify(string username, string password)
    {
        await using var source = ConnectAs(username, password);
        await using var connection = await source.OpenConnectionAsync();
        await using (var command = new NpgsqlCommand("SELECT ssl FROM pg_stat_ssl WHERE pid = pg_backend_pid()", connection))
            if (await command.ExecuteScalarAsync() is not true)
                throw new InvalidOperationException("TLS not active.");
        await using (var command = new NpgsqlCommand("SELECT balance_kobo, currency FROM public.wallets WHERE wallet_id = 'demo-wallet'", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync() || reader.GetInt64(0) < 0 || reader.GetString(1) != "NGN")
                throw new InvalidOperationException("Synthetic wallet validation failed.");
        }
        // Even if permissions are wrong these checks change no wallet data and roll back DDL.
        foreach (var checkCreate in new[] { false, true })
        {
            await using var transaction = await connection.BeginTransactionAsync();
            var denied = false;
            try
            {
                await using var command = checkCreate
                    ? new NpgsqlCommand("CREATE TABLE public.bootstrap_permission_probe(id integer)", connection, transaction)
                    : new NpgsqlCommand("UPDATE public.wallets SET balance_kobo = 0 WHERE false", connection, transaction);
                await command.ExecuteNonQueryAsync();
            }
            catch (PostgresException exception) when (exception.SqlState == "42501")
            {
                denied = true;
            }
            finally
            {
                await transaction.RollbackAsync();
            }
            if (!denied)
                throw new InvalidOperationException("Application has unexpected write privileges.");
        }
        await using var wrongHostname = ConnectAs(username, password, "invalid.novapay.example");
        var hostnameRejected = false;
        try { await using var unexpected = await wrongHostname.OpenConnectionAsync(); }
        catch (NpgsqlException exception) when (exception.InnerException is AuthenticationException) { hostnameRejected = true; }
        if (!hostnameRejected)
            throw new InvalidOperationException("Hostname verification did not reject an invalid hostname.");
        await using var plaintext = ConnectAs(username, password, tls: false);
        var plaintextRejected = false;
        try { await using var unexpected = await plaintext.OpenConnectionAsync(); }
        catch (PostgresException exception) when (exception.SqlState == "28000") { plaintextRejected = true; }
        if (!plaintextRejected)
            throw new InvalidOperationException("Database did not reject a non-TLS connection.");
    }

    stage = "application secret lookup";
    var current = await ReadSecret(appSecret, "AWSCURRENT");
    var pending = await ReadSecret(appSecret, "AWSPENDING");
    if (current is not null && pending is null)
    {
        using var value = JsonDocument.Parse(current.SecretString);
        stage = "existing application credential verification";
        await Verify("novapay_app", value.RootElement.GetProperty("password").GetString()!);
        Console.WriteLine("Already initialized: existing credentials verified; no password rotation or data changes.");
        return 0;
    }
    if (current is not null)
    {
        if (current.VersionId != pending!.VersionId)
            throw new InvalidOperationException("A separate credential rotation is pending; manual review required.");
        // A first version may also be AWSCURRENT. AWSPENDING marks unfinished
        // initialization; consumers must wait until that marker is removed.
    }

    stage = "administrator connection";
    var admin = await ReadSecret(adminSecret, "AWSCURRENT") ?? throw new InvalidOperationException("Administrator secret unavailable.");
    using var adminValue = JsonDocument.Parse(admin.SecretString);
    await using var adminSource = ConnectAs(adminValue.RootElement.GetProperty("username").GetString()!, adminValue.RootElement.GetProperty("password").GetString()!);
    await using var adminConnection = await adminSource.OpenConnectionAsync();
    await using (var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novapay_app')", adminConnection))
        if (await check.ExecuteScalarAsync() is true && pending is null)
            throw new InvalidOperationException("Existing role has no managed credential; manual review required.");

    stage = "pending application credential";
    if (pending is null)
    {
        var secretJson = JsonSerializer.Serialize(new
        {
            engine = "postgres",
            host,
            port = 5432,
            dbname = "novapay",
            username = "novapay_app",
            password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        await secrets.PutSecretValueAsync(new PutSecretValueRequest
        {
            SecretId = appSecret,
            ClientRequestToken = Guid.NewGuid().ToString(),
            SecretString = secretJson,
            VersionStages = ["AWSPENDING"]
        });
        pending = await ReadSecret(appSecret, "AWSPENDING") ?? throw new InvalidOperationException();
    }
    using var appValue = JsonDocument.Parse(pending.SecretString);
    var password = appValue.RootElement.GetProperty("password").GetString()!;

    stage = "transactional database initialization";
    await using (var transaction = await adminConnection.BeginTransactionAsync())
    {
        // Send a SCRAM verifier rather than the raw password in SQL statements.
        await using (var parameter = new NpgsqlCommand("SELECT set_config('novapay.bootstrap_verifier', $1, true)", adminConnection, transaction))
        {
            parameter.Parameters.AddWithValue(ScramVerifier(password));
            await parameter.ExecuteNonQueryAsync();
        }
        var sql = """
            DO $$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novapay_app') THEN
                CREATE ROLE novapay_app LOGIN;
              END IF;
              IF EXISTS (SELECT 1 FROM pg_auth_members WHERE member = (SELECT oid FROM pg_roles WHERE rolname = 'novapay_app')) THEN
                RAISE EXCEPTION 'Unexpected role memberships';
              END IF;
              IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novapay_app'
                AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls)) THEN
                RAISE EXCEPTION 'Unexpected elevated role privileges';
              END IF;
              EXECUTE format('ALTER ROLE novapay_app WITH LOGIN PASSWORD %L', current_setting('novapay.bootstrap_verifier'));
            END $$;
            REVOKE CREATE ON SCHEMA public FROM PUBLIC;
            """;
        sql += "\n" + await File.ReadAllTextAsync("database/schema.sql");
        sql += "\n" + await File.ReadAllTextAsync("database/seed-demo.sql");
        sql += """

            REVOKE ALL ON ALL TABLES IN SCHEMA public FROM novapay_app;
            REVOKE ALL ON SCHEMA public FROM novapay_app;
            GRANT CONNECT ON DATABASE novapay TO novapay_app;
            GRANT USAGE ON SCHEMA public TO novapay_app;
            GRANT SELECT ON public.wallets TO novapay_app;
            """;
        // Reviewed migration runner: SQL comes only from fixed statements and the two
        // checked-in database scripts. No CLI values or credentials are interpolated.
#pragma warning disable CA2100
        await using var command = new NpgsqlCommand(sql, adminConnection, transaction);
#pragma warning restore CA2100
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
    stage = "application credential verification";
    await Verify("novapay_app", password);
    stage = "credential publication";
    var published = await ReadSecret(appSecret, "AWSCURRENT");
    if (published?.VersionId != pending.VersionId)
        await secrets.UpdateSecretVersionStageAsync(new UpdateSecretVersionStageRequest
        {
            SecretId = appSecret,
            VersionStage = "AWSCURRENT",
            MoveToVersionId = pending.VersionId
        });
    await secrets.UpdateSecretVersionStageAsync(new UpdateSecretVersionStageRequest
    {
        SecretId = appSecret,
        VersionStage = "AWSPENDING",
        RemoveFromVersionId = pending.VersionId
    });
    Console.WriteLine("Initialized: wallet schema and seed, read-only application role, verified credential published to Secrets Manager.");
    Console.WriteLine("Passed: verified TLS, wallet SELECT, denied UPDATE/CREATE, rejected invalid hostname, rejected plaintext connection.");
    return 0;
}
catch (Exception exception)
{
    // Exception messages/SQL/SDK responses can contain credentials. Report only safe classifications.
    var code = exception is PostgresException pg ? $"{pg.SqlState} ({pg.Routine})" : exception.GetType().Name;
    Console.Error.WriteLine($"Database bootstrap failed during {stage}; classification: {code}. Credentials and SQL suppressed.");
    return 1;
}

static string ScramVerifier(string password)
{
    const int iterations = 100000;
    var salt = RandomNumberGenerator.GetBytes(16);
    var salted = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
    var clientKey = HMACSHA256.HashData(salted, Encoding.UTF8.GetBytes("Client Key"));
    var storedKey = SHA256.HashData(clientKey);
    var serverKey = HMACSHA256.HashData(salted, Encoding.UTF8.GetBytes("Server Key"));
    CryptographicOperations.ZeroMemory(salted);
    CryptographicOperations.ZeroMemory(clientKey);
    return $"SCRAM-SHA-256${iterations}:{Convert.ToBase64String(salt)}${Convert.ToBase64String(storedKey)}:{Convert.ToBase64String(serverKey)}";
}
