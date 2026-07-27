using System.Net;
using System.Net.Http.Headers;
using ArchIntel.Api.Auth;
using ArchIntel.GraphStore.Contracts;
using ArchIntel.GraphStore.Core;
using ArchIntel.GraphStore.Sqlite;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ArchIntel.Api.Tests;

/// <summary>
/// Authorization matrix (05-rest-api.md Section 9.4: "role x endpoint x repo... confirm
/// cross-repo access is denied"), run with Authentication:Enabled = true — the opposite default
/// from ApiIntegrationTests, which deliberately covers the auth-disabled/local-dev path. Seeded
/// memberships: alice=Owner/default, bob=Viewer/default, carol=Viewer/other-repo (no access to
/// "default"). DevBearerAuthenticationHandler (05-rest-api.md Section 6.1's stand-in) treats the
/// bearer token as the user id verbatim, so "Bearer alice" authenticates as alice.
/// </summary>
public sealed class AuthorizationTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"archapi-authz-it-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var connectionFactory = new SqliteConnectionFactory($"Data Source={_dbPath}");
        await new MigrationRunner(connectionFactory).ApplyAsync();
        var writer = new SqliteGraphWriter(connectionFactory);
        var ids = new IdGenerator();

        var projectId = ids.ProjectId("Test.sln", "Orders.csproj");
        var scan = await writer.BeginScanAsync(new BeginScanRequest { ScanType = ScanType.Full });
        await writer.UpsertProjectAsync(scan, new ProjectDto { ProjectId = projectId, Name = "Orders", Path = "Orders.csproj" });
        await writer.CompleteScanAsync(scan);

        Environment.SetEnvironmentVariable("GraphStore__ConnectionString", _dbPath);
        Environment.SetEnvironmentVariable("Authentication__Enabled", "true");
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();

        var memberships = _factory.Services.GetRequiredService<IRepoMembershipStore>();
        memberships.Upsert(new RepoMembership("alice", "default", RepoRole.Owner));
        memberships.Upsert(new RepoMembership("bob", "default", RepoRole.Viewer));
        memberships.Upsert(new RepoMembership("carol", "other-repo", RepoRole.Viewer));
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Environment.SetEnvironmentVariable("GraphStore__ConnectionString", null);
        Environment.SetEnvironmentVariable("Authentication__Enabled", null);

        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task NoBearerToken_IsUnauthorizedFromViewerEndpoint()
    {
        // No credential at all -> 401 (ASP.NET Core's default challenge behavior); an authenticated
        // user with an insufficient role -> 403 (see ViewerRole_CanRead_ButCannotPostDiagram etc.).
        var response = await _client.GetAsync("/api/v1/repos/default/projects");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ViewerRole_CanRead_ButCannotPostDiagram()
    {
        var read = await Send(HttpMethod.Get, "/api/v1/repos/default/projects", "bob");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var write = await Send(HttpMethod.Post, "/api/v1/repos/default/diagram", "bob",
            new { scope = (string?)null, depth = 1, format = "mermaid" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task OwnerRole_CanCreateInvitation_MaintainerCannot()
    {
        // alice is Owner — can manage membership.
        var ownerCreate = await Send(HttpMethod.Post, "/api/v1/repos/default/invitations", "alice",
            new { email = "teammate@example.com", role = "Viewer" });
        Assert.Equal(HttpStatusCode.OK, ownerCreate.StatusCode);

        // bob is only a Viewer — Owner-gated invitation management must reject him.
        var viewerCreate = await Send(HttpMethod.Post, "/api/v1/repos/default/invitations", "bob",
            new { email = "teammate@example.com", role = "Viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, viewerCreate.StatusCode);
    }

    [Fact]
    public async Task CrossRepoDenial_ViewerOfOtherRepo_CannotAccessDefaultRepo()
    {
        // carol is a Viewer of "other-repo", not "default" — access to "default" must be denied
        // even though she has SOME repo role, proving the check is repo-scoped, not just role-based.
        var response = await Send(HttpMethod.Get, "/api/v1/repos/default/projects", "carol");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ArchitectureHub_JoinRepo_DeniesUserWithoutMembership()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/architecture"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>("carol");
            })
            .Build();

        await connection.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinRepo", "default"));
        Assert.Contains("Not authorized", ex.Message);
    }

    [Fact]
    public async Task ArchitectureHub_JoinRepo_AllowsUserWithMembership()
    {
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/architecture"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>("bob");
            })
            .Build();

        await connection.StartAsync();

        // Throws if denied; reaching here means bob (Viewer of "default") was let in.
        await connection.InvokeAsync("JoinRepo", "default");
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string bearerUserId, object? body = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", bearerUserId) } };
        if (body is not null)
        {
            request.Content = JsonContent(body);
        }

        return await _client.SendAsync(request);
    }

    private static HttpContent JsonContent(object body) => System.Net.Http.Json.JsonContent.Create(body);
}
