param(
    [string] $Version = "0.1.19",
    [string] $Runtime = "win-x64",
    [string] $Configuration = "Release",
    [string] $OutputDirectory = "artifacts",
    [ValidateSet("all", "self-contained", "framework-dependent")]
    [string] $Variant = "all"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$outputRoot = Join-Path $repoRoot $OutputDirectory
$toolsDir = Join-Path $repoRoot ".tools"
$wixExe = Join-Path $toolsDir "wix.exe"

function ConvertTo-WixId {
    param(
        [string] $Prefix,
        [string] $Value
    )

    $safe = [Regex]::Replace($Value, "[^A-Za-z0-9_]", "_")
    if ($safe.Length -gt 48) {
        $safe = $safe.Substring(0, 48)
    }

    $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))).Substring(0, 8)
    return "${Prefix}_${safe}_$hash"
}

function Escape-Xml {
    param([string] $Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function New-RpackInstallerSource {
    param(
        [string] $PublishDir,
        [string] $SourcePath,
        [string] $ProductName,
        [string] $Description
    )

    $knownFiles = @("rpack.exe", "rpack-open.exe", "rpack.ico")
    foreach ($fileName in $knownFiles) {
        $path = Join-Path $PublishDir $fileName
        if (-not (Test-Path $path)) {
            throw "Missing required installer file: $path"
        }
    }

    $extraComponents = New-Object System.Collections.Generic.List[string]
    $extraFiles = Get-ChildItem -Path $PublishDir -File |
        Where-Object { $knownFiles -notcontains $_.Name } |
        Sort-Object Name

    foreach ($file in $extraFiles) {
        $componentId = ConvertTo-WixId "ExtraComponent" $file.Name
        $fileId = ConvertTo-WixId "ExtraFile" $file.Name
        $source = Escape-Xml $file.FullName
        $extraComponents.Add(@"
      <Component Id="$componentId" Guid="*">
        <File Id="$fileId" Source="$source" KeyPath="yes" />
      </Component>
"@)
    }

    $productNameXml = Escape-Xml $ProductName
    $descriptionXml = Escape-Xml $Description
    $rpackExe = Escape-Xml (Join-Path $PublishDir "rpack.exe")
    $rpackOpenExe = Escape-Xml (Join-Path $PublishDir "rpack-open.exe")
    $rpackIcon = Escape-Xml (Join-Path $PublishDir "rpack.ico")
    $extraComponentsXml = $extraComponents -join [Environment]::NewLine

    $wxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package
    Name="$productNameXml"
    Manufacturer="Pawel Potepa"
    Version="$Version"
    UpgradeCode="92E16A72-464A-4431-A488-8CC22471D23D"
    Scope="perMachine">

    <SummaryInformation Description="$descriptionXml" Manufacturer="Pawel Potepa" />
    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="A newer version of rpack is already installed." />
    <MediaTemplate EmbedCab="yes" />

    <Feature Id="MainFeature" Title="$productNameXml" Level="1">
      <ComponentGroupRef Id="RpackComponents" />
    </Feature>
  </Package>

  <Fragment>
    <StandardDirectory Id="ProgramFilesFolder">
      <Directory Id="INSTALLFOLDER" Name="rpack" />
    </StandardDirectory>
  </Fragment>

  <Fragment>
    <ComponentGroup Id="RpackComponents" Directory="INSTALLFOLDER">
      <Component Id="RpackExecutable" Guid="*">
        <File Id="RpackExe" Source="$rpackExe" KeyPath="yes" />
        <Environment
          Id="AddRpackToPath"
          Name="PATH"
          Value="[INSTALLFOLDER]"
          Permanent="no"
          Part="last"
          Action="set"
          System="yes" />
      </Component>
      <Component Id="RpackOpenExecutable" Guid="*">
        <File Id="RpackOpenExe" Source="$rpackOpenExe" KeyPath="yes" />
      </Component>
      <Component Id="RpackIcon" Guid="*">
        <File Id="RpackIconFile" Source="$rpackIcon" KeyPath="yes" />
      </Component>
$extraComponentsXml
      <Component Id="RpackFileAssociation" Guid="*">
        <RegistryValue Root="HKCR" Key=".rpack" Value="rpack.package" Type="string" KeyPath="yes" />
        <RegistryValue Root="HKCR" Key="rpack.package" Value="rpack Package" Type="string" />
        <RegistryValue Root="HKCR" Key="rpack.package\DefaultIcon" Value="&quot;[INSTALLFOLDER]rpack.ico&quot;" Type="string" />
        <RegistryValue Root="HKCR" Key="rpack.package\shell\open" Value="Open with rpack" Type="string" />
        <RegistryValue Root="HKCR" Key="rpack.package\shell\open\command" Value="&quot;[INSTALLFOLDER]rpack-open.exe&quot; &quot;%1&quot;" Type="string" />
        <RegistryValue Root="HKCR" Key="rpack.package\shell\apply-dirty" Value="Apply with rpack allowing dirty" Type="string" />
        <RegistryValue Root="HKCR" Key="rpack.package\shell\apply-dirty" Name="Extended" Value="" Type="string" />
        <RegistryValue Root="HKCR" Key="rpack.package\shell\apply-dirty\command" Value="&quot;[INSTALLFOLDER]rpack-open.exe&quot; --allow-dirty &quot;%1&quot;" Type="string" />
      </Component>
    </ComponentGroup>
  </Fragment>
</Wix>
"@

    Set-Content -Path $SourcePath -Value $wxs -Encoding UTF8
}

function Publish-RpackProject {
    param(
        [string] $ProjectPath,
        [string] $PublishDir,
        [bool] $SelfContained
    )

    $selfContainedValue = $SelfContained.ToString().ToLowerInvariant()
    $singleFileValue = $SelfContained.ToString().ToLowerInvariant()

    dotnet publish $ProjectPath `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained $selfContainedValue `
        -p:PublishSingleFile=$singleFileValue `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        --output $PublishDir
}

function Build-RpackMsi {
    param(
        [string] $VariantName,
        [bool] $SelfContained,
        [string] $Description
    )

    $publishDir = Join-Path $outputRoot "publish\$Runtime-$VariantName"
    $msiPath = Join-Path $outputRoot "rpack-$Version-$Runtime-$VariantName.msi"
    $generatedWxs = Join-Path $outputRoot "installer-$Runtime-$VariantName.wxs"

    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    Publish-RpackProject (Join-Path $repoRoot "src/Rpack.Cli/Rpack.Cli.csproj") $publishDir $SelfContained
    Publish-RpackProject (Join-Path $repoRoot "src/Rpack.Open/Rpack.Open.csproj") $publishDir $SelfContained
    Copy-Item (Join-Path $repoRoot "assets/rpack.ico") (Join-Path $publishDir "rpack.ico") -Force

    New-RpackInstallerSource `
        -PublishDir $publishDir `
        -SourcePath $generatedWxs `
        -ProductName "rpack" `
        -Description $Description

    & $wixExe build $generatedWxs `
        -arch x64 `
        -out $msiPath `
        -pdbtype none

    Write-Host "Created $msiPath"
}

if (-not (Test-Path $wixExe)) {
    dotnet tool install wix --tool-path $toolsDir --version 5.0.2
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

if ($Variant -eq "all" -or $Variant -eq "self-contained") {
    Build-RpackMsi `
        -VariantName "self-contained" `
        -SelfContained $true `
        -Description "rpack Windows Installer (self-contained)"
}

if ($Variant -eq "all" -or $Variant -eq "framework-dependent") {
    Build-RpackMsi `
        -VariantName "framework-dependent" `
        -SelfContained $false `
        -Description "rpack Windows Installer (framework-dependent, requires .NET 10 Desktop Runtime x64)"
}

