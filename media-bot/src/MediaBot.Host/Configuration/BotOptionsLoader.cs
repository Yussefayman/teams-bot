using System;
using Mahdar.MediaBot.Configuration;
using Microsoft.Extensions.Configuration;

namespace Mahdar.MediaBot.Host.Configuration;

/// <summary>
/// Loads BotOptions from appsettings.json then overrides with the environment-variable
/// names from plan §13. Single place that knows env var names; nothing is hardcoded.
/// </summary>
public static class BotOptionsLoader
{
    public static BotOptions Load(IConfiguration config)
    {
        var o = new BotOptions();
        config.GetSection("Bot").Bind(o);   // appsettings.json defaults

        o.BotAppId = Env("BOT_APP_ID", o.BotAppId);
        o.BotAppSecret = Env("BOT_APP_SECRET", o.BotAppSecret);
        o.TenantId = Env("TENANT_ID", o.TenantId);
        o.PublicHostname = Env("PUBLIC_HOSTNAME", o.PublicHostname);
        o.MediaPort = EnvInt("MEDIA_PORT", o.MediaPort);
        o.SttWsBaseUrl = Env("STT_WS_BASE_URL", o.SttWsBaseUrl);
        o.OrchestratorBaseUrl = Env("ORCHESTRATOR_BASE_URL", o.OrchestratorBaseUrl);
        o.DumpAudio = EnvBool("DUMP_AUDIO", o.DumpAudio);
        o.DumpDir = Env("DUMP_DIR", o.DumpDir);
        o.ReconnectBufferSeconds = EnvDouble("RECONNECT_BUFFER_SECONDS", o.ReconnectBufferSeconds);
        o.CertificateThumbprint = Env("CERT_THUMBPRINT", o.CertificateThumbprint);
        return o;
    }

    private static string Env(string key, string fallback)
        => Environment.GetEnvironmentVariable(key) is { Length: > 0 } v ? v : fallback;

    private static int EnvInt(string key, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

    private static double EnvDouble(string key, double fallback)
        => double.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

    private static bool EnvBool(string key, bool fallback)
        => bool.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;
}
