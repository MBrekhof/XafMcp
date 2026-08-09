$root = Split-Path $PSScriptRoot -Parent
if (Test-Path "$root\.app.pid") {
    $appPid = Get-Content "$root\.app.pid"
    taskkill /PID $appPid /F /T 2>$null
    Remove-Item "$root\.app.pid"
}
# safety net: anything still bound to :5210
Get-NetTCPConnection -LocalPort 5210 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { taskkill /PID $_.OwningProcess /F /T 2>$null }
Write-Host 'App stopped'
