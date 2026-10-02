param([ValidateSet('idle','gaze','natural')][string]$Mode = 'idle', [int]$Seconds = 50, [switch]$Eco, [ValidateSet('432','433')][string]$Version = '432')
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path $PSScriptRoot -Parent
$taskExisting = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'YabiDesktopPet%.exe'" | Where-Object { $_.CommandLine -match '--test-' })
if ($taskExisting.Count -gt 0) { throw 'A Yabi GUI test is still running. Run benchmarks serially.' }
$taskOutput = Join-Path $taskProject "bin\validation$Version"
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskArguments = @('--exit-after', "$Seconds")
if ($Mode -eq 'idle') { $taskArguments += '--test-idle' }
if ($Mode -eq 'gaze') { $taskArguments += '--test-gaze' }
if ($Mode -eq 'natural') { $taskArguments += '--test-natural' }
if ($Eco) { $taskArguments += '--test-eco' }
$taskLabel = if ($Eco) { "$Mode-eco" } else { $Mode }
$taskProcess = Start-Process -FilePath (Join-Path $taskProject "bin\YabiDesktopPet$Version.exe") -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
$taskStarted = $taskProcess.StartTime
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
$taskSamples | Export-Csv -LiteralPath (Join-Path $taskOutput "benchmark$Version-$taskLabel.csv") -NoTypeInformation -Encoding UTF8
$taskSteady = @($taskSamples | Where-Object elapsed -GE 20)
$taskCpu = 0
if ($taskSteady.Count -gt 1) { $taskCpu = 100 * ($taskSteady[-1].cpu - $taskSteady[0].cpu) / ($taskSteady[-1].elapsed - $taskSteady[0].elapsed) }
$taskResult = [pscustomobject]@{
    date = (Get-Date).ToString('o'); mode = $taskLabel; exitCode = $taskProcess.ExitCode
    durationSeconds = [Math]::Round($taskWatch.Elapsed.TotalSeconds, 2)
    meanWorkingMiB = [Math]::Round(($taskSteady | Measure-Object workingMiB -Average).Average, 2)
    peakWorkingMiB = ($taskSamples | Measure-Object workingMiB -Maximum).Maximum
    meanPrivateMiB = [Math]::Round(($taskSteady | Measure-Object privateMiB -Average).Average, 2)
    averageCpuOneCorePercent = [Math]::Round($taskCpu, 2)
    allResponding = (@($taskSamples | Where-Object { -not $_.responding }).Count -eq 0)
}
$taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput "benchmark$Version-$taskLabel.json") -Encoding UTF8
$taskStartup = Join-Path ([IO.Path]::GetTempPath()) 'YabiDesktopPet-v4.3-validation\startup.txt'
if ((Get-Item -LiteralPath $taskStartup).LastWriteTime -ge $taskStarted) { Copy-Item -LiteralPath $taskStartup -Destination (Join-Path $taskOutput "startup-$taskLabel.txt") }
if ($Mode -eq 'gaze') {
    $taskEvidence = Join-Path ([IO.Path]::GetTempPath()) $(if ($Version -eq '433') { 'YabiDesktopPet-v4.3.3-gaze' } else { 'YabiDesktopPet-v4.3.2-gaze' })
    $taskTarget = Join-Path $taskOutput $taskLabel
    $taskResultFile = Get-Item -LiteralPath (Join-Path $taskEvidence 'result.txt')
    if ($taskResultFile.LastWriteTime -lt $taskStarted -or (Get-Content -LiteralPath $taskResultFile.FullName -Raw) -notmatch 'integration: PASS') {
        throw 'Gaze regression failed or did not produce fresh evidence.'
    }
    New-Item -ItemType Directory -Force -Path $taskTarget | Out-Null
    Get-ChildItem -LiteralPath $taskEvidence -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskTarget }
}
if ($Mode -eq 'natural') {
    $taskTrace = Join-Path ([IO.Path]::GetTempPath()) 'YabiDesktopPet-v4.3-playback.log'
    if (Test-Path -LiteralPath $taskTrace) { Copy-Item -LiteralPath $taskTrace -Destination (Join-Path $taskOutput 'playback-natural.log') }
}
$taskResult | ConvertTo-Json
if ($taskProcess.ExitCode -ne 0) { throw "Test process exited with $($taskProcess.ExitCode)." }
