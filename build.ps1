param(
    [switch]$Console,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot 'XMG_ADB.csproj'
$msbuild = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'

if (-not (Test-Path $msbuild)) {
    $msbuild = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe'
}
if (-not (Test-Path $msbuild)) {
    $command = Get-Command 'MSBuild.exe' -ErrorAction SilentlyContinue
    if ($command) { $msbuild = $command.Source }
}
if (-not (Test-Path $msbuild)) {
    throw 'MSBuild was not found. Install .NET Framework 4.8 Developer Pack or Visual Studio Build Tools.'
}

$consoleBuild = if ($Console) { 'true' } else { 'false' }
$arguments = @(
    $projectFile,
    '/nologo',
    '/verbosity:minimal',
    '/target:Rebuild',
    "/property:Configuration=$Configuration",
    "/property:ConsoleBuild=$consoleBuild"
)

$targetingPack = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
if (-not (Test-Path $targetingPack)) {
    $frameworkPath = Split-Path -Parent $msbuild
    Write-Warning '.NET Framework 4.8 Targeting Pack was not found; using installed runtime reference assemblies.'
    $arguments += "/property:FrameworkPathOverride=$frameworkPath"
    $arguments += '/property:ResolveAssemblyWarnOrErrorOnTargetArchitectureMismatch=None'
}

& $msbuild $arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$outputName = if ($Console) { 'XMG_ADB.Tests.exe' } else { 'XMG_ADB.exe' }
$outputPath = Join-Path (Join-Path $projectRoot 'dist') $outputName
Write-Output $outputPath
