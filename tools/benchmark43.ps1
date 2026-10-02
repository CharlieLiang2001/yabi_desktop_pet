param([string]$Mode = 'idle', [int]$Seconds = 50, [switch]$Eco)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path $PSScriptRoot -Parent
$taskOutput = Join-Path $taskProject 'bin'
$taskArguments = @('--test-ui', '--exit-after', "$Seconds")
if ($Mode -eq 'idle') { $taskArguments += '--test-idle' }
if ($Mode -eq 'cycle') { $taskArguments += '--test-cycle' }
if ($Mode -eq 'natural') { $taskArguments += '--test-natural' }
if ($Eco) { $taskArguments += '--test-eco' }
$taskLabel = if ($Eco) { "$Mode-eco" } else { $Mode }
$taskProcess = Start-Process -FilePath (Join-Path $taskOutput 'YabiDesktopPet43.exe') -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
$taskWatch = [Diagnostics.Stopwatch]::StartNew()
$taskSamples = [Collections.Generic.List[object]]::new()
while (-not $taskProcess.HasExited) {
    $taskProcess.Refresh()
    if ($taskProcess.HasExited) { break }
    $taskSamples.Add([pscustomobject]@{
        elapsed = [Math]::Round($taskWatch.Elapsed.TotalSeconds, 3)
        cpu = $taskProcess.TotalProcessorTime.TotalSeconds
        workingMiB = [Math]::Round($taskProcess.WorkingSet64 / 1MB, 2)
        privateMiB = [Math]::Round($taskProcess.PrivateMemorySize64 / 1MB, 2)
        responding = $taskProcess.Responding
    })
    Start-Sleep -Milliseconds 1000
}
$taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput "benchmark43-$taskLabel.csv") -NoTypeInformation -Encoding UTF8
$taskSteady = @($taskSamples | Where-Object elapsed -GE 20)
$taskCpu = 0
if ($taskSteady.Count -gt 1) {
    $taskCpu = 100 * ($taskSteady[-1].cpu - $taskSteady[0].cpu) / ($taskSteady[-1].elapsed - $taskSteady[0].elapsed)
}
$taskResult = [pscustomobject]@{
    date = (Get-Date).ToString('o'); mode = $taskLabel; exitCode = $taskProcess.ExitCode
    durationSeconds = [Math]::Round($taskWatch.Elapsed.TotalSeconds, 2)
    meanWorkingMiB = [Math]::Round(($taskSteady | Measure-Object workingMiB -Average).Average, 2)
    peakWorkingMiB = ($taskSamples | Measure-Object workingMiB -Maximum).Maximum
    meanPrivateMiB = [Math]::Round(($taskSteady | Measure-Object privateMiB -Average).Average, 2)
    averageCpuOneCorePercent = [Math]::Round($taskCpu, 2)
    allResponding = (@($taskSamples | Where-Object { -not $_.responding }).Count -eq 0)
}
$taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput "benchmark43-$taskLabel.json") -Encoding UTF8
$taskResult | ConvertTo-Json
