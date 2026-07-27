using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ArchIntel.Api.Auth;

/// <summary>Stand-in for Better Auth's JWKS-validated bearer tokens (05-rest-api.md Section 6.1 —
/// whether Better Auth can issue a token this API validates directly, or whether the Next.js
/// dashboard needs to act as an auth proxy, is an open question the doc itself doesn't resolve,
/// and no OAuth app credentials or Node auth service exist anywhere in this repo).
///
/// Trusts an `Authorization: Bearer &lt;userId&gt;` header verbatim as the caller's identity — this
/// is NOT real authentication. It exists only so the authorization layer built on top of it
/// (RepoRole, RepoMembership, the RequireRepoViewer/Maintainer/Owner policies) is real, working,
/// and testable today. Swapping in real Better Auth/OAuth later is an isolated change: replace
/// this one handler's HandleAuthenticateAsync with real token validation; nothing downstream
/// needs to change since it only ever consumes the resulting ClaimsPrincipal.</summary>
public sealed class DevBearerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DevBearer";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = header["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
