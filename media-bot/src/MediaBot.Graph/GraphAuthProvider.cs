using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Mahdar.MediaBot.Configuration;
using Microsoft.Graph.Communications.Client.Authentication;
using Microsoft.Identity.Client;

namespace Mahdar.MediaBot.Graph;

/// <summary>
/// WINDOWS-ONLY. App-only (client-credentials) auth for the Graph Communications client.
///
/// Outbound: acquires a v2 app token for graph .default via MSAL and stamps it on every
/// request the SDK makes (this is what authorizes Calls().AddAsync to join a meeting).
/// MSAL caches and refreshes the token internally.
///
/// Inbound: validates that incoming call notifications were issued by Microsoft for this
/// app. The current bot only initiates calls outbound (POST /api/joinCall), so inbound
/// notifications are not on the join critical path; full signing-key validation against
/// Graph's OpenID metadata must be wired here before exposing /api/calling publicly.
/// See graph-comms-samples AuthenticationProvider for the reference implementation.
/// </summary>
public sealed class GraphAuthProvider : IRequestAuthenticationProvider
{
    private const string GraphDefaultScope = "https://graph.microsoft.com/.default";

    private readonly IConfidentialClientApplication _app;

    public GraphAuthProvider(BotOptions opts)
    {
        if (string.IsNullOrEmpty(opts.BotAppId)) throw new ArgumentException("BOT_APP_ID is required");
        if (string.IsNullOrEmpty(opts.BotAppSecret)) throw new ArgumentException("BOT_APP_SECRET is required");
        if (string.IsNullOrEmpty(opts.TenantId)) throw new ArgumentException("TENANT_ID is required");

        _app = ConfidentialClientApplicationBuilder.Create(opts.BotAppId)
            .WithClientSecret(opts.BotAppSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{opts.TenantId}"))
            .Build();
    }

    public async Task AuthenticateOutboundRequestAsync(HttpRequestMessage request, string tenant)
    {
        var result = await _app
            .AcquireTokenForClient(new[] { GraphDefaultScope })
            .ExecuteAsync()
            .ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
    }

    public Task<RequestValidationResult> ValidateInboundRequestAsync(HttpRequestMessage request)
    {
        // TODO(graph-sdk, VM): validate the inbound token's signature/issuer/audience against
        // Graph's OpenID configuration before trusting /api/calling notifications. Outbound-
        // initiated joins do not exercise this path.
        var tenant = string.IsNullOrEmpty(request.Headers.Authorization?.Parameter) ? null : (string?)null;
        return Task.FromResult(new RequestValidationResult { IsValid = false, TenantId = tenant });
    }
}
