<#
.SYNOPSIS
  A tiny OpenAI-compatible endpoint for tests. Replies with a fixed analysis (one cited claim, one uncited claim,
  and an "unknowns" list), serves /v1/models, and appends one JSON line per request to -LogPath so tests can assert
  what the app sent.
#>
param(
    [int]$Port = 18480,
    [Parameter(Mandatory)][string]$LogPath
)

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()

function Send($ctx, $obj) {
    $body = $obj | ConvertTo-Json -Depth 8 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $ctx.Response.ContentType = 'application/json'
    $ctx.Response.ContentLength64 = $bytes.Length
    $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
    $ctx.Response.Close()
}

try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $path = $ctx.Request.Url.AbsolutePath

        if ($path -eq '/v1/models') {
            ([ordered]@{ path = $path; auth = [string]$ctx.Request.Headers['Authorization'] } | ConvertTo-Json -Compress) | Add-Content $LogPath -Encoding utf8
            Send $ctx @{ data = @(@{ id = 'test-model' }, @{ id = 'other-model' }) }
            continue
        }

        $reader = New-Object System.IO.StreamReader($ctx.Request.InputStream, [Text.Encoding]::UTF8)
        $req = $reader.ReadToEnd() | ConvertFrom-Json
        $system = [string]$req.messages[0].content
        $user = [string]$req.messages[1].content
        $entry = [ordered]@{
            path           = $path
            auth           = [string]$ctx.Request.Headers['Authorization']
            model          = $req.model
            hasTools       = [bool]($req.PSObject.Properties.Name -contains 'tools')
            userLen        = $user.Length
            leaked         = [bool]($user -match 'SAMPLE-DEVICE-NAME|SAMPLE-BOARD|\\Users\\')
            noQuestionRule = [bool]($system -match 'NEVER ask the user a question')
            hasSuggestions = [bool]($user -match '"suggestions"')
        }
        ($entry | ConvertTo-Json -Compress) | Add-Content $LogPath -Encoding utf8

        $content = @{
            summary = 'Two long freezes while the GPU stayed busy.'
            inconclusive = $false
            findings = @(
                @{ title = 'Freezes look GPU-side'; explanation = 'The GPU was busy during both freezes.'; evidence = @('finding:FREEZES', 'freeze.count'); actions = @('Record another run with a setting changed.'); confidence = 'medium' },
                @{ title = 'Made-up claim'; explanation = 'No evidence.'; evidence = @('invented.metric'); actions = @(); confidence = 'high' }
            )
            unknowns = @('Whether VSync was on during the capture')
        } | ConvertTo-Json -Depth 6 -Compress
        Send $ctx @{ choices = @(@{ message = @{ role = 'assistant'; content = $content } }) }
    }
}
finally {
    $listener.Stop()
}
