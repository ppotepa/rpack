param(
    [string] $Version = "0.1.4",
    [string] $Runtime = "win-x64",
    [string] $Configuration = "Release",
    [string] $OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputRoot = Join-Path $repoRoot $OutputDirectory
$publishDir = Join-Path $outputRoot "publish\$Runtime"
$msiPath = Join-Path $outputRoot "rpack-$Version-$Runtime.msi"
$toolsDir = Join-Path $repoRoot ".tools"
$wixExe = Join-Path $toolsDir "wix.exe"

if (-not (Test-Path $wixExe)) {
    dotnet tool install wix --tool-path $toolsDir --version 5.0.2
}

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src/Rpack.Cli/Rpack.Cli.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    --output $publishDir

& $wixExe build (Join-Path $repoRoot "installer/windows/Product.wxs") `
    -arch x64 `
    -d "ProductVersion=$Version" `
    -d "PublishDir=$publishDir" `
    -out $msiPath `
    -pdbtype none

Write-Host "Created $msiPath"
