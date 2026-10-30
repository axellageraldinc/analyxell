<#
.SYNOPSIS
  Regenerates the README screenshots in docs\images from synthetic captures (no real data), capturing only the
  app window. Run from the repository root:  powershell -File tests\ui\Take-Screenshots.ps1
#>
$ErrorActionPreference = 'Continue'
$env:WINAPP_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$work = Join-Path $root 'artifacts\screenshots'
$images = Join-Path $root 'docs\images'
New-Item -ItemType Directory -Force $work, $images | Out-Null

$a = Join-Path $work 'before.json'; $b = Join-Path $work 'after.json'
& (Join-Path $PSScriptRoot 'New-SampleCapture.ps1') -Path $a -Freezes 5 -FrameMs 16.5 -Noise 0.22 -DurationSec 300 -Seed 3 | Out-Null
& (Join-Path $PSScriptRoot 'New-SampleCapture.ps1') -Path $b -Freezes 0 -FrameMs 12.8 -Noise 0.12 -DurationSec 300 -Seed 11 | Out-Null

# The app keeps its settings (and the encrypted AI key) under the package folder. Set the real ones aside so this
# run starts clean and can never overwrite or delete them; they are put back at the end.
$appData = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Filter 'axellageraldinc.Daignostic_*' -Directory -ErrorAction SilentlyContinue |
    ForEach-Object { Join-Path $_.FullName 'LocalCache\Local\Daignostic' } | Select-Object -First 1
$appDataBackup = Join-Path $env:TEMP 'daignostic-appdata-backup'
Remove-Item $appDataBackup -Recurse -Force -ErrorAction SilentlyContinue
if ($appData -and (Test-Path $appData)) { Copy-Item $appData $appDataBackup -Recurse -Force }

Get-Process Daignostic -ErrorAction SilentlyContinue | Stop-Process -Force
Push-Location (Join-Path $root 'src\Daignostic')
$run = & winapp run . --arch x64 --clean --detach --json --args "`"$a`" `"$b`"" 2>&1 | Out-String
Pop-Location
if ($run -notmatch 'ProcessId') { Write-Host $run; exit 1 }
Start-Sleep 9

function Shot($name) {
    $h = (& winapp ui list-windows -a Daignostic 2>&1 | Out-String) -replace "$([char]27)\[[0-9;]*m", ''
    if ("$h" -match 'HWND (\d+): "[^"]*" \(window,') { & winapp ui screenshot -w $Matches[1] -o (Join-Path $images "$name.png") 2>&1 | Out-Null }
}

# Show the "before" run on the Analyze page: it has the interesting findings.
& winapp ui invoke NavCompare -a Daignostic 2>&1 | Out-Null; Start-Sleep 2
Shot 'compare'
& winapp ui invoke NavAnalyze -a Daignostic 2>&1 | Out-Null; Start-Sleep 2
Shot 'analyze'
& winapp ui invoke TabFindings -a Daignostic 2>&1 | Out-Null; Start-Sleep 2
Shot 'analyze-details'
# Remove what the test saved, then put the user's own settings back.
Get-Process Daignostic -ErrorAction SilentlyContinue | Stop-Process -Force
if ($appData) {
    Remove-Item $appData -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $appDataBackup) { New-Item -ItemType Directory -Force (Split-Path $appData) | Out-Null; Copy-Item $appDataBackup $appData -Recurse -Force; Remove-Item $appDataBackup -Recurse -Force }
}
Get-ChildItem $images -Filter *.png | Select-Object Name, Length
