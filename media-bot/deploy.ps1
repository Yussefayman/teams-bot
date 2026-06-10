# media-bot deployment for the Windows VM (Component A). Run in an elevated PowerShell.
# Builds the Windows configuration (incl. MediaBot.Graph) and publishes the Host.
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutDir = "publish"
)
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot

Write-Host "Restoring + publishing MediaBot.Host ($Configuration, $Runtime)..."
dotnet publish src/MediaBot.Host/MediaBot.Host.csproj `
    -c $Configuration -r $Runtime --self-contained false -o $OutDir

Write-Host ""
Write-Host "Published to $OutDir."
Write-Host "Next (see docs/SETUP-AZURE.md):"
Write-Host "  1. Set environment variables (CALL_SOURCE=graph, BOT_APP_ID, TENANT_ID,"
Write-Host "     PUBLIC_HOSTNAME, CERT_THUMBPRINT, MEDIA_PORT, STT_WS_BASE_URL, ORCHESTRATOR_BASE_URL)."
Write-Host "  2. Bind the TLS cert to the media port (netsh http add sslcert ...)."
Write-Host "  3. Open ports 443 and the media TCP port in the NSG + Windows firewall."
Write-Host "  4. Run: $OutDir\Mahdar.MediaBot.Host.exe   (or install as a Windows service)."

Pop-Location
