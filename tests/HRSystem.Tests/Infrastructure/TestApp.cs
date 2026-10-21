using System.Collections.Concurrent;
using HRSystem.Application.Interfaces;
using HRSystem.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace HRSystem.Tests.Infrastructure;

/// <summary>
/// Hosts the real application (migrations, seeding, DI) against a throw-away SQL Server
/// database created from <see cref="ConnectionVariable"/> and dropped afterwards.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ConnectionVariable = "HRSYSTEM_TEST_SQL";

    private string? _databaseName;
    private string? _serverConnection;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    public FakeEmailService Emails { get; } = new();

    public async Task InitializeAsync()
    {
        if (!IsConfigured) return;

        _serverConnection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConnectionVariable))
        {
            InitialCatalog = "master",
            TrustServerCertificate = true
        }.ConnectionString;
        _databaseName = $"HRSystem_Test_{Guid.NewGuid():N}"[..30];

        await WithRetryAsync(() => ExecuteAsync($"CREATE DATABASE [{_databaseName}]"));

        // Program reads the connection string while building the host, so it must be in the environment first.
        var appConnection = new SqlConnectionStringBuilder(_serverConnection)
        {
            InitialCatalog = _databaseName,
            MultipleActiveResultSets = true
        }.ConnectionString;
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", appConnection);

        _ = Server; // starts the host: runs migrations and the seeder
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.AddScoped<TestCurrentUser>();
            services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>());
            services.AddSingleton<IEmailService>(Emails);
        });
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_databaseName is null) return;

        SqlConnection.ClearAllPools();
        await ExecuteAsync($"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}];");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(_serverConnection);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // A freshly started SQL Server container accepts TCP before it accepts logins.
    private static async Task WithRetryAsync(Func<Task> action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { await action(); return; }
            catch (SqlException) when (attempt < 30) { await Task.Delay(TimeSpan.FromSeconds(2)); }
        }
    }
}

/// <summary>
/// Lets a test act as a given user inside a DI scope; falls back to the HTTP user for web requests.
/// </summary>
public sealed class TestCurrentUser : ICurrentUser
{
    private readonly CurrentUser _http;
    private (string UserId, bool IsAdmin, HashSet<string> Permissions)? _acting;

    public TestCurrentUser(IHttpContextAccessor accessor) => _http = new CurrentUser(accessor);

    public void ActAs(string userId, params string[] permissions) => _acting = (userId, false, permissions.ToHashSet());
    public void ActAsAdmin(string userId) => _acting = (userId, true, new HashSet<string>());

    public string? UserId => _acting?.UserId ?? _http.UserId;
    public string? UserName => _acting is null ? _http.UserName : _acting.Value.UserId;
    public bool IsAdmin => _acting?.IsAdmin ?? _http.IsAdmin;
    public bool HasPermission(string permission)
        => _acting is { } a ? a.IsAdmin || a.Permissions.Contains(permission) : _http.HasPermission(permission);
}

public sealed class FakeEmailService : IEmailService
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Enqueue(EmailMessage message) => _sent.Enqueue(message);
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<TestApp>
{
    public const string Name = "integration";
}
