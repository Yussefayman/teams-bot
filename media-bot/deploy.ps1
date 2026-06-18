# media-bot deployment for the Windows VM (Component A). Run in an elevated PowerShell.
# The Host + Graph are .NET Framework (net472) — the Skype media SDK is net472-only —
# so this is a framework-dependent publish (no -r/--self-contained). The .NET Framework
# 4.7.2+ runtime must already be installed on the VM (it is, on Windows Server 2019+).
param(
    [string]$Configuration = "Release",
    [string]$OutDir = "publish"
)
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

Write-Host "Restoring + publishing MediaBot.Host ($Configuration, net472)..."
dotnet publish src/MediaBot.Host/MediaBot.Host.csproj -c $Configuration -o $OutDir

Write-Host ""
Write-Host "Published to $OutDir."
Write-Host "Next (see docs/SETUP-AZURE.md):"
Write-Host "  0. One-time VM prerequisites (media SDK will not initialize without them):"
Write-Host "       - VM has >= 2 vCPUs (e.g. Standard_D4s_v5)."
Write-Host "       - Install-WindowsFeature Server-Media-Foundation   (then reboot)."
Write-Host "       - Visual C++ 2015-2022 x64 redistributable (vc_redist.x64.exe)."
Write-Host "  1. Set environment variables (CALL_SOURCE=graph, BOT_APP_ID, BOT_APP_SECRET,"
Write-Host "     TENANT_ID, PUBLIC_HOSTNAME, CERT_THUMBPRINT, MEDIA_PORT, STT_WS_BASE_URL,"
Write-Host "     ORCHESTRATOR_BASE_URL). HTTP_PREFIX defaults to https://+:443/."
Write-Host "  2. Bind the TLS cert to the HTTP API port for http.sys (HttpListener serves"
Write-Host "     https via http.sys, not a Kestrel pfx). Using the same CERT_THUMBPRINT:"
Write-Host "       netsh http add sslcert ipport=0.0.0.0:443 certhash=<CERT_THUMBPRINT> ``"
Write-Host "         appid={a1b2c3d4-0000-0000-0000-000000000001}"
Write-Host "     and reserve the URL ACL:  netsh http add urlacl url=https://+:443/ user=Everyone"
Write-Host "  3. Open ports 443 and the media TCP port (MEDIA_PORT) in the NSG + Windows firewall."
Write-Host "  4. Run: $OutDir\Mahdar.MediaBot.Host.exe   (or install as a Windows service)."

Pop-Location
