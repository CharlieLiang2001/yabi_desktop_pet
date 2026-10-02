param(
    [switch]$RebuildAssets
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$sourceRoot = Join-Path $projectRoot 'src'
$assetArchive = Join-Path $projectRoot 'assets\generated\yabi-animations-v43.zip'
$iconPath = Join-Path $projectRoot 'assets\generated\yabi-v4.ico'
$manifestPath = Join-Path $projectRoot 'app.manifest'
$binRoot = Join-Path $projectRoot 'bin'
$outputPath = Join-Path $binRoot 'YabiDesktopPet434.exe'

if ($RebuildAssets -or -not (Test-Path -LiteralPath $assetArchive)) {
    & python (Join-Path $projectRoot 'tools\build_assets43.py')
    if ($LASTEXITCODE -ne 0) {
        throw 'Asset build failed.'
    }
}

New-Item -ItemType Directory -Force -Path $binRoot | Out-Null

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @(
    'C:\Windows\Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll',
    'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll',
    'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll',
    'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll',
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll',
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll',
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xml.Linq.dll',
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.dll',
    'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.FileSystem.dll'
)

foreach ($required in @($csc, $assetArchive, $iconPath, $manifestPath) + $references) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing build input: $required"
    }
}

$compilerArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/debug-',
    '/warn:4',
    "/out:$outputPath",
    "/win32icon:$iconPath",
    "/win32manifest:$manifestPath",
    "/resource:$assetArchive,Yabi.Assets.Animations.v4.zip"
)
foreach ($reference in $references) {
    $compilerArguments += "/reference:$reference"
}
$compilerArguments += Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName }

& $csc $compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw "C# compiler failed with exit code $LASTEXITCODE."
}

Get-Item -LiteralPath $outputPath | Select-Object FullName, Length, LastWriteTime
