<#
 Runs MindLift for a public demo through ngrok.
   .\scripts\start-ngrok-demo.ps1                      # random ngrok address
   .\scripts\start-ngrok-demo.ps1 -Domain my-demo.ngrok-free.app
 Stop the app that is already running on port 5082 first. Close this window to end the demo.
#>
param([string]$Domain = "")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command ngrok -ErrorAction SilentlyContinue)) { throw "ngrok is not installed or not on PATH. Install it and run: ngrok config add-authtoken <your token>" }
if (Get-NetTCPConnection -LocalPort 5082 -State Listen -ErrorAction SilentlyContinue) { throw "Something is already listening on port 5082. Stop it first (it is the app that is running now)." }

$args = @("http", "5082")
if ($Domain) { $args += "--url=https://$Domain" }
$tunnel = Start-Process ngrok -ArgumentList $args -PassThru -WindowStyle Minimized

$url = $null
for ($i = 0; $i -lt 30 -and -not $url; $i++) {
    Start-Sleep -Seconds 1
    try { $url = (Invoke-RestMethod http://127.0.0.1:4040/api/tunnels).tunnels | Where-Object { $_.public_url -like "https*" } | Select-Object -First 1 -ExpandProperty public_url } catch { }
}
if (-not $url) { Stop-Process -Id $tunnel.Id -ErrorAction SilentlyContinue; throw "ngrok did not give an address. Is the auth token set?" }

# Links in emails use App:BaseUrl; ngrok connects from this machine, so only loopback is trusted as the proxy (real visitor addresses, one sign-in rate limit each).
$env:App__BaseUrl = $url
$env:ForwardedHeaders__Enabled = "true"
$env:ForwardedHeaders__KnownProxies__0 = "127.0.0.1"
$env:ForwardedHeaders__KnownProxies__1 = "::1"
$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Host "Share this address: $url" -ForegroundColor Green
try { dotnet run --project "$root\src\MindLift.Api" --launch-profile http } finally { Stop-Process -Id $tunnel.Id -ErrorAction SilentlyContinue }
