<#
.SYNOPSIS
  End-to-end UI smoke test. Builds and launches the app, then drives it with UI Automation (winapp ui).

.DESCRIPTION
  Uses synthetic captures and a local mock AI endpoint, so it needs no game, no CapFrameX and no AI key.
  It checks that the Analyze, Compare and Settings pages work, that the AI request is the previewed one, carries
  no tools and leaks no machine identifiers, and that uncited AI claims are discarded.

  Requires: WinApp CLI 0.7+, .NET SDK 10, Developer Mode. Run:  powershell -File tests\ui\smoke.ps1
  Screenshots are written to artifacts\ui-test.
#>
param(
    [int]$MockPort = 18480,
    [string]$Arch = 'x64'
)

$ErrorActionPreference = 'Continue'
$env:WINAPP_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$work = Join-Path $root 'artifacts\ui-test'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $work | Out-Null
$failures = New-Object System.Collections.Generic.List[string]

function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { Write-Host "  PASS  $name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $name $detail" -ForegroundColor Red; $failures.Add($name) }
}

function Ui {
    param([string[]]$UiArgs)
    $out = & winapp ui @UiArgs -a Daignostic 2>&1 | Out-String
    return $out
}

function HasText([string]$text) { return (Ui @('search', $text)) -match 'Found [1-9]' }

function Shot([string]$name) {
    $h = (& winapp ui list-windows -a Daignostic 2>&1 | Out-String)
    if ("$h" -match 'HWND (\d+): "[^"]*" \(window,') { & winapp ui screenshot -w $Matches[1] --capture-screen -o (Join-Path $work "$name.png") 2>&1 | Out-Null }
}

function WaitFor([string]$text, [int]$seconds = 20) {
    for ($i = 0; $i -lt $seconds; $i++) { if (HasText $text) { return $true }; Start-Sleep 1 }
    return $false
}

Write-Host 'Preparing synthetic captures and mock AI endpoint'
$a = Join-Path $work 'baseline.json'; $b = Join-Path $work 'later.json'
& (Join-Path $PSScriptRoot 'New-SampleCapture.ps1') -Path $a -Freezes 4 -FrameMs 12 | Out-Null
& (Join-Path $PSScriptRoot 'New-SampleCapture.ps1') -Path $b -Freezes 0 -FrameMs 9 | Out-Null
$mockLog = Join-Path $work 'mock.log'
$mock = Start-Job -ScriptBlock { param($s, $p, $l) & $s -Port $p -LogPath $l } -ArgumentList (Join-Path $PSScriptRoot 'Start-MockAiServer.ps1'), $MockPort, $mockLog
Start-Sleep 2

Get-Process Daignostic -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'Building and launching the app'
Push-Location (Join-Path $root 'src\Daignostic')
$run = & winapp run . --arch $Arch --clean --detach --json --args "`"$a`" `"$b`"" 2>&1 | Out-String
Pop-Location
if ($run -notmatch 'ProcessId') { Write-Host $run; Stop-Job $mock; exit 2 }

try {
    Write-Host 'Analyze page'
    Check 'capture opens from the command line' (WaitFor 'Sample Game' 30)
    Check 'metric cards are shown' ((HasText 'Average') -and (HasText 'Freezes'))
    # The later capture (no freezes) is loaded last, so it is the one shown on the Analyze page.
    Check 'findings are shown for the selected capture' (HasText 'Frame delivery looks smooth')
    Check 'device name is not displayed' (-not (HasText 'SAMPLE-DEVICE-NAME'))
    Shot '1-analyze'

    Write-Host 'Compare page'
    Ui @('invoke', 'NavCompare') | Out-Null; Start-Sleep 2
    Check 'comparison summary is shown' (WaitFor 'B is better on' 10)
    Check 'freezes are compared between the runs' (HasText 'Freezes (200 ms or more)')
    Check 'context rows are not counted as better or worse' (HasText 'Context')
    Shot '2-compare'

    Write-Host 'Settings page and AI connection'
    Ui @('invoke', 'SettingsItem') | Out-Null; Start-Sleep 2
    Ui @('set-value', 'EndpointBox', "http://127.0.0.1:$MockPort") | Out-Null
    Ui @('invoke', 'TestAiButton') | Out-Null
    Check 'AI connection test succeeds' (WaitFor 'The AI endpoint answered' 10)
    Ui @('invoke', 'SaveSettingsButton') | Out-Null; Start-Sleep 1
    Check 'settings can be saved' (HasText 'Settings saved')
    Shot '3-settings'

    Write-Host 'AI analysis'
    Ui @('invoke', 'NavAnalyze') | Out-Null; Start-Sleep 2
    Ui @('scroll-into-view', 'AskAiButton') | Out-Null; Start-Sleep 1
    Ui @('invoke', 'AskAiButton') | Out-Null
    Check 'cited AI claim is shown' (WaitFor 'Freezes look GPU-side' 15)
    Check 'uncited AI claim is discarded and flagged' ((-not (HasText 'Made-up claim')) -and (HasText 'discarded because they cited no measured evidence'))
    Shot '4-ai'

    Write-Host 'What the AI endpoint received'
    $requests = @(Get-Content $mockLog -ErrorAction SilentlyContinue | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
    $analysis = $requests | Where-Object { $_.userLen -gt 100 } | Select-Object -Last 1
    Check 'an analysis request was sent' ($null -ne $analysis)
    Check 'request offered the model no tools' (($null -ne $analysis) -and (-not $analysis.hasTools))
    Check 'request leaked no device name, board or user path' (($null -ne $analysis) -and (-not $analysis.leaked))
}
finally {
    Get-Process Daignostic -ErrorAction SilentlyContinue | Stop-Process -Force
    # The test saved a mock endpoint into the app's settings; remove the app's own settings so nothing leaks into real use.
    Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Filter 'axellageraldinc.Daignostic_*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item (Join-Path $_.FullName 'LocalCache\Local\Daignostic') -Recurse -Force -ErrorAction SilentlyContinue }
    Stop-Job $mock -ErrorAction SilentlyContinue
    Remove-Job $mock -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($failures.Count -eq 0) { Write-Host "All UI checks passed. Screenshots: $work" -ForegroundColor Green; exit 0 }
Write-Host "$($failures.Count) UI check(s) failed: $($failures -join '; ')" -ForegroundColor Red
exit 1
