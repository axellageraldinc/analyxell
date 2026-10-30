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

# The app keeps its settings (and the encrypted AI key) in two places: the package's own folder, and the plain
# %LOCALAPPDATA%\Analyxell folder that the portable build uses (a packaged app reads through to it when its own
# folder is empty). Set both aside so this run starts clean and can never overwrite or delete the real ones;
# they are put back at the end.
$dataDirs = @(
    (Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Packages') -Filter 'axellageraldinc.Analyxell_*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Join-Path $_.FullName 'LocalCache\Local\Analyxell' } | Select-Object -First 1),
    (Join-Path $env:LOCALAPPDATA 'Analyxell')
) | Where-Object { $_ }
$backupRoot = Join-Path $env:TEMP 'analyxell-appdata-backup'
Remove-Item $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
Get-Process Analyxell -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
for ($i = 0; $i -lt $dataDirs.Count; $i++) {
    if (Test-Path $dataDirs[$i]) {
        Copy-Item $dataDirs[$i] (Join-Path $backupRoot $i) -Recurse -Force
        Remove-Item $dataDirs[$i] -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Get-Process Analyxell -ErrorAction SilentlyContinue | Stop-Process -Force
Push-Location (Join-Path $root 'src\Analyxell')
$run = & winapp run . --arch x64 --clean --detach --json --args "`"$a`" `"$b`"" 2>&1 | Out-String
Pop-Location
if ($run -notmatch 'ProcessId') { Write-Host $run; exit 1 }
Start-Sleep 9

function Shot($name) {
    $h = (& winapp ui list-windows -a Analyxell 2>&1 | Out-String) -replace "$([char]27)\[[0-9;]*m", ''
    if ("$h" -match 'HWND (\d+): "[^"]*" \(window,') { & winapp ui screenshot -w $Matches[1] -o (Join-Path $images "$name.png") 2>&1 | Out-Null }
}

# Show the "before" run on the Analyze page: it has the interesting findings.
& winapp ui invoke NavCompare -a Analyxell 2>&1 | Out-Null; Start-Sleep 2
Shot 'compare'
& winapp ui invoke NavAnalyze -a Analyxell 2>&1 | Out-Null; Start-Sleep 2
Shot 'analyze'
& winapp ui invoke TabFindings -a Analyxell 2>&1 | Out-Null; Start-Sleep 2
Shot 'analyze-details'
# Remove what the test saved, then put the user's own settings back.
Get-Process Analyxell -ErrorAction SilentlyContinue | Stop-Process -Force
for ($i = 0; $i -lt $dataDirs.Count; $i++) {
    Remove-Item $dataDirs[$i] -Recurse -Force -ErrorAction SilentlyContinue
    $saved = Join-Path $backupRoot $i
    if (Test-Path $saved) { New-Item -ItemType Directory -Force (Split-Path $dataDirs[$i]) | Out-Null; Copy-Item $saved $dataDirs[$i] -Recurse -Force }
}
Remove-Item $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $images -Filter *.png | Select-Object Name, Length
