<#
.SYNOPSIS
  A tiny OpenAI-compatible endpoint for tests. Replies with a fixed analysis (one cited claim, one uncited claim)
  and appends one JSON line per request to -LogPath so tests can assert what the app sent.
#>
param(
    [int]$Port = 18480,
    [Parameter(Mandatory)][string]$LogPath
)

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $reader = New-Object System.IO.StreamReader($ctx.Request.InputStream, [Text.Encoding]::UTF8)
        $req = $reader.ReadToEnd() | ConvertFrom-Json
        $user = $req.messages[1].content
        $entry = [ordered]@{
            path     = $ctx.Request.Url.AbsolutePath
            auth     = [string]$ctx.Request.Headers['Authorization']
            model    = $req.model
            hasTools = [bool]($req.PSObject.Properties.Name -contains 'tools')
            userLen  = $user.Length
            leaked   = [bool]($user -match 'SAMPLE-DEVICE-NAME|SAMPLE-BOARD|\\Users\\')
        }
        ($entry | ConvertTo-Json -Compress) | Add-Content $LogPath -Encoding utf8

        $content = @{
            summary = 'Two long freezes while the GPU stayed busy.'
            inconclusive = $false
            findings = @(
                @{ title = 'Freezes look GPU-side'; explanation = 'The GPU was busy during both freezes.'; evidence = @('finding:FREEZES', 'freeze.count'); actions = @('Record another run with a setting changed.'); confidence = 'medium' },
                @{ title = 'Made-up claim'; explanation = 'No evidence.'; evidence = @('invented.metric'); actions = @(); confidence = 'high' }
            )
        } | ConvertTo-Json -Depth 6 -Compress
        $body = @{ choices = @(@{ message = @{ role = 'assistant'; content = $content } }) } | ConvertTo-Json -Depth 8 -Compress
        $bytes = [Text.Encoding]::UTF8.GetBytes($body)
        $ctx.Response.ContentType = 'application/json'
        $ctx.Response.ContentLength64 = $bytes.Length
        $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
        $ctx.Response.Close()
    }
}
finally {
    $listener.Stop()
}
