# Unity MCP (mcp-for-unity v3.4.7, HTTP+SSE) 直连调用器
# 用法： . E:\Projects_Unity\MiHoYo\.dsh-scratch\unity-mcp.ps1
#        Invoke-McpTool 'read_console' '{"action":"get"}'
$script:McpBase = 'http://127.0.0.1:8080/mcp'
$script:McpSessionFile = Join-Path $env:TEMP 'unity-mcp-session.txt'

function Get-SseJson([string]$content) {
    if (-not $content) { return $null }
    $data = ($content -split "`n" |
        Where-Object { $_ -match '^data:\s*' } |
        ForEach-Object { $_ -replace '^data:\s*', '' }) -join ''
    if (-not $data) { return $null }
    try { return $data | ConvertFrom-Json } catch { return $null }
}

function Invoke-McpRaw([string]$body, [string]$sessionId) {
    $headers = @{ Accept = 'application/json, text/event-stream' }
    if ($sessionId) { $headers['Mcp-Session-Id'] = $sessionId }
    $r = Invoke-WebRequest -Uri $script:McpBase -Method POST -Body $body `
        -ContentType 'application/json' -Headers $headers -TimeoutSec 300 -UseBasicParsing
    $sid = $r.Headers['mcp-session-id']
    if ($sid -is [array]) { $sid = $sid[0] }
    return [pscustomobject]@{
        Status  = [int]$r.StatusCode
        Session = $sid
        Json    = (Get-SseJson $r.Content)
        Content = $r.Content
    }
}

function Connect-Mcp([switch]$Force) {
    if (-not $Force -and (Test-Path $script:McpSessionFile)) {
        $cached = (Get-Content $script:McpSessionFile -Raw).Trim()
        if ($cached) {
            try {
                $probe = Invoke-McpRaw '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' $cached
                if ($probe.Json -and $probe.Json.result -and $probe.Json.result.tools) {
                    $script:McpSession = $cached
                    return $cached
                }
            } catch { }
        }
    }

    $init = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"dsh-agent","version":"1.0"}}}'
    $res = Invoke-McpRaw $init $null
    $script:McpSession = $res.Session
    Set-Content -Path $script:McpSessionFile -Value $script:McpSession
    Invoke-McpRaw '{"jsonrpc":"2.0","method":"notifications/initialized"}' $script:McpSession | Out-Null
    return $script:McpSession
}

function Get-McpSession() {
    if (-not $script:McpSession) { Connect-Mcp | Out-Null }
    return $script:McpSession
}

function Invoke-Mcp([string]$method, [string]$paramsJson) {
    $sid = Get-McpSession
    if (-not $paramsJson) { $paramsJson = '{}' }
    $body = '{"jsonrpc":"2.0","id":' + (Get-Random -Maximum 100000) + ',"method":"' + $method + '","params":' + $paramsJson + '}'
    $res = Invoke-McpRaw $body $sid
    return $res.Json
}

function Invoke-McpTool([string]$name, [string]$argsJson) {
    if (-not $argsJson) { $argsJson = '{}' }
    $body = '{"jsonrpc":"2.0","id":' + (Get-Random -Maximum 100000) + ',"method":"tools/call","params":{"name":"' + $name + '","arguments":' + $argsJson + '}}'
    $sid = Get-McpSession
    $res = Invoke-McpRaw $body $sid
    if (-not $res.Json) { return "(empty response) $($res.Content)" }
    if ($res.Json.error) { return "MCP-ERROR: $($res.Json.error | ConvertTo-Json -Depth 6 -Compress)" }
    $text = ($res.Json.result.content | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join "`n"
    return $text
}

function Get-McpTools() {
    $r = Invoke-Mcp 'tools/list' '{}'
    return $r.result.tools
}
