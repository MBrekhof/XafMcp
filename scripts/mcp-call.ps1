param(
    [string]$Tool,
    [string]$ArgsJson = '{}',
    [switch]$List,
    [string]$BaseUrl = 'http://localhost:5210/mcp'
)
$ErrorActionPreference = 'Stop'

function Parse-McpBody([string]$body) {
    # Streamable HTTP may answer application/json or an SSE frame ("event: message\ndata: {...}")
    $jsonLine = if ($body -match '(?m)^data: (.+)$') { $Matches[1] } else { $body }
    return $jsonLine | ConvertFrom-Json
}

$headers = @{ 'Accept' = 'application/json, text/event-stream' }

$init = @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{
    protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'mcp-call'; version = '1.0' } } } | ConvertTo-Json -Depth 8
$r = Invoke-WebRequest $BaseUrl -Method Post -Body $init -ContentType 'application/json' -Headers $headers -UseBasicParsing
if ($r.Headers['mcp-session-id']) { $headers['mcp-session-id'] = @($r.Headers['mcp-session-id'])[0] }

$initialized = @{ jsonrpc = '2.0'; method = 'notifications/initialized' } | ConvertTo-Json
Invoke-WebRequest $BaseUrl -Method Post -Body $initialized -ContentType 'application/json' -Headers $headers -UseBasicParsing | Out-Null

if ($List) {
    $req = @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} } | ConvertTo-Json -Depth 4
    $resp = Parse-McpBody (Invoke-WebRequest $BaseUrl -Method Post -Body $req -ContentType 'application/json' -Headers $headers -UseBasicParsing).Content
    $resp.result.tools | ForEach-Object { $_.name }
    exit 0
}

$call = @{ jsonrpc = '2.0'; id = 2; method = 'tools/call'; params = @{
    name = $Tool; arguments = ($ArgsJson | ConvertFrom-Json) } } | ConvertTo-Json -Depth 16
$resp = Parse-McpBody (Invoke-WebRequest $BaseUrl -Method Post -Body $call -ContentType 'application/json' -Headers $headers -UseBasicParsing).Content
if ($resp.error) { Write-Error ("MCP error: " + ($resp.error | ConvertTo-Json -Depth 8)) }
if ($resp.result.isError) { Write-Error ("Tool error: " + (($resp.result.content | Where-Object type -eq 'text').text -join "`n")) }
($resp.result.content | Where-Object type -eq 'text').text
