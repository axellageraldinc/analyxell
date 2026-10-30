<#
.SYNOPSIS
  Writes a synthetic CapFrameX-format capture for UI tests. Contains no real machine or game data.

.PARAMETER Path
  Output .json path.

.PARAMETER Freezes
  Number of 300 ms freezes to insert (spread evenly).

.PARAMETER FrameMs
  Steady frame time in milliseconds.
#>
param(
    [Parameter(Mandatory)][string]$Path,
    [int]$Freezes = 2,
    [double]$FrameMs = 10.0,
    [int]$DurationSec = 60,
    [double]$Noise = 0.0,
    [int]$Seed = 7
)

$rng = New-Object System.Random $Seed

$frames = New-Object System.Collections.Generic.List[double]
$freezeEvery = if ($Freezes -gt 0) { [math]::Floor($DurationSec / ($Freezes + 1)) } else { [int]::MaxValue }
$elapsed = 0.0; $nextFreeze = $freezeEvery; $placed = 0
while ($elapsed -lt $DurationSec) {
    if ($placed -lt $Freezes -and $elapsed -ge $nextFreeze) {
        $frames.Add(300.0); $elapsed += 0.3; $nextFreeze += $freezeEvery; $placed++
    }
    else {
        $ms = $FrameMs * (1.0 + $Noise * ($rng.NextDouble() * 2 - 1))
        if ($Noise -gt 0 -and $rng.NextDouble() -lt 0.004) { $ms = $FrameMs * (2.2 + 2 * $rng.NextDouble()) }
        $frames.Add([math]::Round($ms, 3)); $elapsed += $ms / 1000.0
    }
}

$times = New-Object double[] $frames.Count
$acc = 0.0
for ($i = 0; $i -lt $frames.Count; $i++) { $acc += $frames[$i] / 1000.0; $times[$i] = [math]::Round($acc, 4) }

$grid = 0..([int]($DurationSec / 0.25) + 4) | ForEach-Object { $_ * 0.25 }
function Series($name, $type, $value) {
    @{ Name = $name; Type = $type; StableIdentifier = $null; Values = @($grid | ForEach-Object { $value }) }
}

$sensors = [ordered]@{
    MeasureTime         = @{ Name = 'MeasureTime'; Type = 'Time'; StableIdentifier = $null; Values = @($grid) }
    '/gpu-nvidia/0/load/0' = Series 'GPU Core' 'Load' 94
    '/amdcpu/0/load/1'     = Series 'CPU Max' 'Load' 55
    '/gpu-nvidia/0/data/3' = Series 'GPU Memory Dedicated' 'Data' 6.5
    '/gpu-nvidia/0/data/2' = Series 'GPU Memory Total' 'Data' 12
    '/nvme/1/load/33'      = Series 'Drive Total Activity' 'Load' 2
}

$capture = [ordered]@{
    Hash = 'sample'
    Info = [ordered]@{
        AppVersion = '1.9.1.5'; GameName = 'Sample Game'; ProcessName = 'sample.exe'; Processor = 'Sample CPU 8-Core'
        DeviceName = 'SAMPLE-DEVICE-NAME'; Motherboard = 'SAMPLE-BOARD'; OS = 'Sample OS'; SystemRam = '32GB'
        GPUDriverVersion = '1.0'; GPU = 'Sample GPU 12GB'; ApiInfo = 'DX12'; PresentationMode = 'Fullscreen'
        ResolutionInfo = '2560x1440'; CreationDate = '2026-01-01T00:00:00Z'
    }
    Runs = @(@{
        Hash = 'run'
        CaptureData = [ordered]@{
            TimeInSeconds = @($times | ForEach-Object { $_ })
            MsBetweenPresents = $frames.ToArray()
            MsBetweenDisplayChange = $frames.ToArray()
            GpuActive = @($frames | ForEach-Object { [math]::Round($_ * 0.94, 3) })
        }
        SensorData2 = $sensors
    })
}

New-Item -ItemType Directory -Force (Split-Path $Path -Parent) | Out-Null
$capture | ConvertTo-Json -Depth 10 -Compress | Set-Content $Path -Encoding utf8
Write-Host "Wrote $Path ($($frames.Count) frames, $Freezes freeze(s))"
