param([int]$TimeoutSec = 90)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path "$root\.app.pid") { & "$PSScriptRoot\stop-app.ps1" }
$proc = Start-Process dotnet -ArgumentList 'run','--project',"$root\XafMcp.Blazor.Server" -PassThru -WindowStyle Hidden -WorkingDirectory $root
$proc.Id | Set-Content "$root\.app.pid"
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest 'http://localhost:5210/LoginPage' -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { Write-Host "App up (pid $($proc.Id))"; exit 0 }
    } catch { Start-Sleep -Milliseconds 750 }
}
& "$PSScriptRoot\stop-app.ps1"
Write-Error 'App did not come up in time'
