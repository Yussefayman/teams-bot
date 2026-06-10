namespace Mahdar.MediaBot.Configuration;

/// <summary>
/// All media-bot configuration. Bound from appsettings.json then overridden by
/// environment variables (see Program.cs). No value is hardcoded elsewhere.
/// Env var names match plan §13.
/// </summary>
public sealed class BotOptions
{
    public string BotAppId { get; set; } = "";        // BOT_APP_ID  (= Entra app client id)
    public string BotAppSecret { get; set; } = "";    // BOT_APP_SECRET
    public string TenantId { get; set; } = "";        // TENANT_ID
    public string PublicHostname { get; set; } = "";  // PUBLIC_HOSTNAME (FQDN with valid TLS cert)
    public int MediaPort { get; set; } = 8445;        // MEDIA_PORT
    public string SttWsBaseUrl { get; set; } = "";    // STT_WS_BASE_URL  e.g. wss://stt-host
    public string OrchestratorBaseUrl { get; set; } = ""; // ORCHESTRATOR_BASE_URL e.g. https://orch-host
    public bool DumpAudio { get; set; }               // DUMP_AUDIO=true -> write per-call .wav
    public string DumpDir { get; set; } = "data/dumps";

    public double ReconnectBufferSeconds { get; set; } = 60; // §4.3 ring buffer cap
    public string CertificateThumbprint { get; set; } = ""; // CERT_THUMBPRINT (media TLS, set on Windows)
}
