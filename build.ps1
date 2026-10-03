param(
    [switch]$Console
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $projectRoot 'src'
$distRoot = Join-Path $projectRoot 'dist'
$compiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $compiler)) {
    $compiler = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $compiler)) {
    throw 'The .NET Framework C# compiler was not found.'
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
$outputName = if ($Console) { 'XMG_ADB.Tests.exe' } else { 'XMG_ADB.exe' }
$target = if ($Console) { 'exe' } else { 'winexe' }
$outputPath = Join-Path $distRoot $outputName
$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
$frameworkRoot = Split-Path -Parent $compiler
$wpfRoot = Join-Path $frameworkRoot 'WPF'

$arguments = @(
    '/nologo',
    '/optimize+',
    '/platform:anycpu',
    "/target:$target",
    "/out:$outputPath",
    "/win32manifest:$(Join-Path $projectRoot 'app.manifest')",
    "/r:$(Join-Path $wpfRoot 'WindowsBase.dll')",
    "/r:$(Join-Path $wpfRoot 'PresentationCore.dll')",
    "/r:$(Join-Path $wpfRoot 'PresentationFramework.dll')",
    "/r:$(Join-Path $frameworkRoot 'System.Xaml.dll')",
    "/r:$(Join-Path $frameworkRoot 'System.Windows.Forms.dll')",
    "/r:$(Join-Path $frameworkRoot 'System.Drawing.dll')",
    "/r:$(Join-Path $frameworkRoot 'System.Core.dll')",
    "/r:$(Join-Path $frameworkRoot 'System.Security.dll')"
) + $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Output $outputPath
