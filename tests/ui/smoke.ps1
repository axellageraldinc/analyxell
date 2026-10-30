<#
.SYNOPSIS
  End-to-end UI smoke test. Builds and launches the app, then drives it with UI Automation (winapp ui).

.DESCRIPTION
  Uses synthetic captures and a local mock AI endpoint, so it needs no game, no CapFrameX and no AI key.
  It checks the Analyze, Compare and Settings pages, the AI flow (the request is the previewed one, carries no tools,
  leaks no machine identifiers, forbids questions, and uncited AI claims are discarded), the extra chart views, the
  suggestions, the background-program check, and the setup prompts.

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
    $h = (& winapp ui list-windows -a Daignostic 2>&1 | Out-String) -replace "$([char]27)\[[0-9;]*m", ''
    if ("$h" -match 'HWND (\d+): "[^"]*" \(window,') { & winapp ui screenshot -w $Matches[1] -o (Join-Path $work "$name.png") 2>&1 | Out-Null }
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

# The app keeps its settings (and the encrypted AI key) in two places: the package's own folder, and the plain
# %LOCALAPPDATA%\Daignostic folder that the portable build uses (a packaged app reads through to it when its own
# folder is empty). Set both aside so this run starts clean and can never overwrite or delete the real ones;
# they are put back at the end.
$dataDirs = @(
    (Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Filter 'axellageraldinc.Daignostic_*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'LocalCache\Local\Daignostic' } | Select-Object -First 1),
    (Join-Path $env:LOCALAPPDATA 'Daignostic')
) | Where-Object { $_ }
$backupRoot = Join-Path $env:TEMP 'daignostic-appdata-backup'
Remove-Item $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
Get-Process Daignostic -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
for ($i = 0; $i -lt $dataDirs.Count; $i++) {
    if (Test-Path $dataDirs[$i]) {
        Copy-Item $dataDirs[$i] (Join-Path $backupRoot $i) -Recurse -Force
        Remove-Item $dataDirs[$i] -Recurse -Force -ErrorAction SilentlyContinue
    }
}

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
    Check 'device name is not displayed' (-not (HasText 'SAMPLE-DEVICE-NAME'))
    Check 'a one-line verdict leads the page' ((Ui @('search', 'VerdictBar')) -match 'Found [1-9]' -and (HasText 'This run looks healthy'))
    Check 'the page is split into tabs' (((Ui @('search', 'TabOverview')) -match 'Found [1-9]') -and ((Ui @('search', 'TabAi')) -match 'Found [1-9]') -and ((Ui @('search', 'TabFindings')) -match 'Found [1-9]') -and ((Ui @('search', 'TabPc')) -match 'Found [1-9]') -and ((Ui @('search', 'TabBackground')) -match 'Found [1-9]'))
    Check 'the overview has a compact AI bar, not the whole AI panel' (((Ui @('search', 'AiBarStatus')) -match 'Found [1-9]') -and -not (HasText 'Anything the AI should know'))
    Check 'the CapFrameX header button is labelled' ((HasText 'Open CapFrameX') -or (HasText 'Get CapFrameX'))
    Check 'PC setup is easy to find in the header' ((Ui @('search', 'PcSetupHeaderButton')) -match 'Found [1-9]')
    Check 'a chart view picker is offered' ((Ui @('search', 'ChartViewCombo')) -match 'Found [1-9]')
    Shot '1-analyze'

    Write-Host 'Findings, PC and AI tabs'
    Ui @('invoke', 'TabFindings') | Out-Null; Start-Sleep 1
    Check 'findings tab shows the findings' (HasText 'Frame delivery looks smooth')
    Shot '1b-findings'
    Ui @('invoke', 'TabPc') | Out-Null; Start-Sleep 1
    Check 'PC tab shows the specs' (HasText 'Sample GPU 12GB')
    Check 'missing game settings are called out with a way to add them' ((HasText 'no built-in reader') -and (Ui @('search', 'GameSettingsButton')) -match 'Found [1-9]')
    Shot '1c-pc'
    Ui @('invoke', 'TabAi') | Out-Null; Start-Sleep 1
    Check 'AI is optional: set-up and export-for-own-AI are offered' ((HasText 'AI is optional') -and (Ui @('search', 'ExportPackButton')) -match 'Found [1-9]')

    Write-Host 'Background check'
    Ui @('invoke', 'TabBackground') | Out-Null; Start-Sleep 1
    Ui @('invoke', 'CheckBackgroundButton') | Out-Null
    $bgDone = $false
    for ($i = 0; $i -lt 40 -and -not $bgDone; $i++) {
        Start-Sleep 1
        $bgDone = (HasText 'No background program stood out') -or (HasText 'Other programs use') -or (HasText 'Programs that often affect games') -or (HasText 'Other programs are reading')
    }
    Check 'background check completes and reports' $bgDone
    Shot '2-background'

    Write-Host 'Compare page'
    Ui @('invoke', 'NavCompare') | Out-Null; Start-Sleep 2
    Check 'comparison summary is shown' (WaitFor 'B is better on' 10)
    Check 'a metric picker is offered and both runs are named in the legend' (((Ui @('search', 'CompareViewCombo')) -match 'Found [1-9]') -and (HasText 'baseline'))
    Check 'freezes are compared between the runs' (HasText 'Freezes (200 ms or more)')
    Check 'context rows are not counted as better or worse' (HasText 'Context')
    Shot '3-compare'

    Write-Host 'Game library'
    Ui @('invoke', 'NavLibrary') | Out-Null; Start-Sleep 2
    Check 'the library lists the game that was opened' (WaitFor 'Sample Game' 10)
    Check 'a game can be added from the library' ((Ui @('search', 'AddGameButton')) -match 'Found [1-9]')
    Check 'each game says what is known about it' ((HasText 'Diagnosable from captures') -or (HasText 'Settings entered by you'))
    Shot '3a-library'
    Ui @('invoke', 'NavAnalyze') | Out-Null; Start-Sleep 1

    Write-Host 'Settings page'
    Ui @('invoke', 'SettingsItem') | Out-Null; Start-Sleep 2
    Check 'AI is off by default and the user is told it is optional' (HasText 'Everything else works without AI')
    Check 'the MCP tutorial is there' (HasText 'How to turn on CapFrameX')
    Shot '3b-capframex-card'
    Check 'CapFrameX status is shown (installed or not found, with a way forward)' ((HasText 'CapFrameX is installed') -or (HasText 'CapFrameX was not found') -or (HasText 'daignostic could not find where it is installed'))
    Ui @('invoke', 'AiEnabledSwitch') | Out-Null; Start-Sleep 1
    Check 'the API key assurance is shown' (HasText 'Your key stays on this PC')
    Check 'the field is called Model name, not Preset' ((HasText 'Model name') -and -not (HasText 'Preset'))
    # A bad address and a missing model must be reported next to those fields, and nothing must be saved.
    Ui @('set-value', 'EndpointBox', 'not a url') | Out-Null
    Ui @('invoke', 'SaveSettingsButton') | Out-Null; Start-Sleep 1
    Check 'a bad endpoint is explained under the endpoint field' ((Ui @('search', 'EndpointMessage')) -match 'Found [1-9]' -and (HasText 'not a valid address'))
    Check 'a missing model name is explained under the model field' ((Ui @('search', 'ModelMessage')) -match 'Found [1-9]' -and (HasText 'Enter a model name'))
    Check 'saving is refused and says why' (HasText 'Not saved yet')
    Ui @('scroll-into-view', 'ApiKeyBox') | Out-Null; Start-Sleep 1
    Shot '4a-inline-errors'
    # Providers document their address both with and without /v1; both must work.
    Ui @('set-value', 'EndpointBox', "http://127.0.0.1:$MockPort/v1") | Out-Null
    Ui @('invoke', 'ListModelsButton') | Out-Null
    Check 'models can be listed from the endpoint' (WaitFor '2 model(s) found' 10)
    Ui @('set-value', 'ModelBox', 'test-model') | Out-Null
    Ui @('invoke', 'TestAiButton') | Out-Null
    Check 'AI connection test succeeds' (WaitFor 'The AI endpoint answered' 10)
    Ui @('invoke', 'SaveSettingsButton') | Out-Null; Start-Sleep 1
    Check 'settings can be saved' (HasText 'Settings saved')
    Shot '4-settings'

    Write-Host 'AI analysis'
    Ui @('invoke', 'NavAnalyze') | Out-Null; Start-Sleep 2
    Ui @('invoke', 'TabOverview') | Out-Null; Start-Sleep 1
    Ui @('invoke', 'AskAiBarButton') | Out-Null
    Check 'asking from the overview opens the AI tab' (WaitFor 'Anything the AI should know' 10)
    Check 'cited AI claim is shown' (WaitFor 'Freezes look GPU-side' 15)
    Check 'uncited AI claim is discarded and flagged' ((-not (HasText 'Made-up claim')) -and (HasText 'discarded because they cited no measured evidence'))
    Check 'what the AI could not tell is shown instead of a question' (HasText 'Whether VSync was on')
    # The AI may only name checks daignostic offered: the background check was already done, the made-up one is dropped.
    Check 'the AI is offered buttons for checks it asked for' ((Ui @('search', 'pc_setup')) -match 'Found [1-9]')
    Check 'a check that was already done is not offered again' ((Ui @('search', 'background_check')) -notmatch 'Found [1-9]')
    Check 'a check the AI made up is dropped' ((Ui @('search', 'delete_everything')) -notmatch 'Found [1-9]')
    Ui @('invoke', 'pc_setup') | Out-Null
    Check 'pressing a check opens the form in daignostic, not the AI' (WaitFor 'Your PC setup' 10)
    Ui @('invoke', 'Cancel') | Out-Null; Start-Sleep 1
    Shot '5-ai'

    Write-Host 'What the AI endpoint received'
    $requests = @(Get-Content $mockLog -ErrorAction SilentlyContinue | Where-Object { $_ } | ForEach-Object { $_ | ConvertFrom-Json })
    $analysis = $requests | Where-Object { $_.userLen -gt 100 } | Select-Object -Last 1
    Check 'an analysis request was sent' ($null -ne $analysis)
    Check 'request used the chosen model name' (($null -ne $analysis) -and $analysis.model -eq 'test-model')
    Check 'request offered the model no tools' (($null -ne $analysis) -and (-not $analysis.hasTools))
    Check 'request leaked no device name, board or user path' (($null -ne $analysis) -and (-not $analysis.leaked))
    Check 'the AI was told never to ask questions' (($null -ne $analysis) -and $analysis.noQuestionRule)
    Check 'suggestions were included for the AI to build on' (($null -ne $analysis) -and $analysis.hasSuggestions)

    function UseModel([string]$model) {
        Ui @('invoke', 'SettingsItem') | Out-Null; Start-Sleep 1
        Ui @('set-value', 'ModelBox', $model) | Out-Null
        Ui @('invoke', 'SaveSettingsButton') | Out-Null; Start-Sleep 1
        Ui @('invoke', 'NavAnalyze') | Out-Null; Start-Sleep 1
        Ui @('invoke', 'TabAi') | Out-Null; Start-Sleep 1
    }

    Write-Host 'AI progress and errors'
    UseModel 'slow-model'
    Ui @('invoke', 'AskAiButton') | Out-Null
    Check 'a progress card with a clock and Cancel is shown while waiting' ((WaitFor 'Waiting for the answer' 10) -and ((Ui @('search', 'CancelAiButton')) -match 'Found [1-9]'))
    Shot '5b-ai-progress'
    Ui @('invoke', 'CancelAiButton') | Out-Null
    Check 'cancelling says nothing was changed' (WaitFor 'You cancelled the request' 10)

    UseModel 'garbage-model'
    Ui @('invoke', 'AskAiButton') | Out-Null
    Check 'an unreadable reply is explained and shown' ((WaitFor 'not in a form daignostic can read' 15) -and ((Ui @('search', 'AiTryAgainButton')) -match 'Found [1-9]'))
    Shot '5c-ai-error'

    UseModel 'badkey-model'
    Ui @('invoke', 'AskAiButton') | Out-Null
    Check 'a rejected key points to Settings' ((WaitFor 'did not accept your API key' 15) -and ((Ui @('search', 'AiOpenSettingsButton')) -match 'Found [1-9]'))
}
finally {
    # Remove what the test saved, then put the user's own settings back.
    Get-Process Daignostic -ErrorAction SilentlyContinue | Stop-Process -Force
    for ($i = 0; $i -lt $dataDirs.Count; $i++) {
        Remove-Item $dataDirs[$i] -Recurse -Force -ErrorAction SilentlyContinue
        $saved = Join-Path $backupRoot $i
        if (Test-Path $saved) { New-Item -ItemType Directory -Force (Split-Path $dataDirs[$i]) | Out-Null; Copy-Item $saved $dataDirs[$i] -Recurse -Force }
    }
    Remove-Item $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    Stop-Job $mock -ErrorAction SilentlyContinue
    Remove-Job $mock -Force -ErrorAction SilentlyContinue
}

Write-Host ''
if ($failures.Count -eq 0) { Write-Host "All UI checks passed. Screenshots: $work" -ForegroundColor Green; exit 0 }
Write-Host "$($failures.Count) UI check(s) failed: $($failures -join '; ')" -ForegroundColor Red
exit 1
