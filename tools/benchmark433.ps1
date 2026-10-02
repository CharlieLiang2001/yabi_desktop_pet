param([ValidateSet('idle','gaze','natural')][string]$Mode = 'gaze', [int]$Seconds = 135, [switch]$Eco)
& (Join-Path $PSScriptRoot 'benchmark432.ps1') -Mode $Mode -Seconds $Seconds -Eco:$Eco -Version '433'
