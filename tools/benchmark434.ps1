param([ValidateSet('idle','gaze','natural')][string]$Mode = 'idle', [int]$Seconds = 50, [switch]$Eco)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path $PSScriptRoot -Parent
$existing = @(Get-CimInstance Win32_Process -Filter "Name LIKE 'YabiDesktopPet%.exe'" | Where-Object { $_.CommandLine -match '--test-' })
if ($existing.Count) { throw 'Another GUI regression is running; run serially.' }
$folder = Join-Path $taskProject 'bin\validation434'
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$label = if ($Eco) { "$Mode-eco" } else { $Mode }
$arguments = @("--test-$Mode", '--exit-after', "$Seconds")
if ($Eco) { $arguments += '--test-eco' }
$probe = Start-Process -FilePath (Join-Path $taskProject 'bin\YabiDesktopPet434.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
$started = $probe.StartTime
$watch = [Diagnostics.Stopwatch]::StartNew()
$samples = [Collections.Generic.List[object]]::new()
while (-not $probe.HasExited) {
    $probe.Refresh()
    if ($probe.HasExited) { break }
    $samples.Add([pscustomobject]@{ elapsed=$watch.Elapsed.TotalSeconds; cpu=$probe.TotalProcessorTime.TotalSeconds; workingMiB=$probe.WorkingSet64/1MB; privateMiB=$probe.PrivateMemorySize64/1MB; responding=$probe.Responding })
    Start-Sleep -Milliseconds 1000
}
$samples | Export-Csv -LiteralPath (Join-Path $folder "benchmark434-$label.csv") -NoTypeInformation -Encoding UTF8
$steady = @($samples | Where-Object elapsed -GE 20)
$cpu = 0
if ($steady.Count -gt 1) { $cpu=100*($steady[-1].cpu-$steady[0].cpu)/($steady[-1].elapsed-$steady[0].elapsed) }
$result = [pscustomobject]@{ date=(Get-Date).ToString('o'); mode=$label; exitCode=$probe.ExitCode; durationSeconds=$watch.Elapsed.TotalSeconds; meanWorkingMiB=[Math]::Round(($steady | Measure-Object workingMiB -Average).Average,2); peakWorkingMiB=($samples | Measure-Object workingMiB -Maximum).Maximum; meanPrivateMiB=[Math]::Round(($steady | Measure-Object privateMiB -Average).Average,2); averageCpuOneCorePercent=[Math]::Round($cpu,2); allResponding=(@($samples | Where-Object { -not $_.responding }).Count -eq 0) }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder "benchmark434-$label.json") -Encoding UTF8
$evidence = Join-Path ([IO.Path]::GetTempPath()) 'YabiDesktopPet-v4.3-validation'
$startup = Join-Path $evidence 'startup.txt'
if ((Test-Path $startup) -and (Get-Item $startup).LastWriteTime -ge $started) { Copy-Item -LiteralPath $startup -Destination (Join-Path $folder "startup-$label.txt") }
if ($Mode -ne 'idle') {
    if ($Mode -eq 'gaze') { $evidence = Join-Path ([IO.Path]::GetTempPath()) 'YabiDesktopPet-v4.3.3-gaze'; $check='result.txt'; $pass='integration: PASS' }
    else { $check='native-regression.txt'; $pass='native regression: PASS' }
    $checkPath = Join-Path $evidence $check
    if (-not (Test-Path $checkPath) -or (Get-Item $checkPath).LastWriteTime -lt $started -or (Get-Content $checkPath -Raw) -notmatch $pass) { throw "Missing fresh $Mode PASS evidence." }
    $target=Join-Path $folder $label
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem -LiteralPath $evidence -File | Where-Object LastWriteTime -ge $started | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target }
}
$result | ConvertTo-Json
if ($probe.ExitCode -ne 0) { throw "Test exited with $($probe.ExitCode)." }
