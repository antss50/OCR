[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[1-9][0-9]*\.[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $Version,

    [ValidateSet('stable', 'beta')]
    [string] $Channel = 'stable',

    [string] $AppInstallerVersion,

    [ValidateSet('win-x64')]
    [string] $Runtime = 'win-x64',

    [Parameter(Mandatory = $true)]
    [uri] $ReleaseBaseUri,

    [string] $Publisher = 'CN=LexVerse Development',

    [string] $PublisherDisplayName = 'LexVerse',

    [string] $CertificateThumbprint,

    [uri] $TimestampUri = 'http://timestamp.digicert.com',

    [switch] $Unsigned,

    [switch] $AllowInsecureReleaseUri
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-WindowsSdkTool {
    param([Parameter(Mandatory = $true)][string] $Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem -Path $kitsRoot -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($null -eq $tool) {
        throw "$Name was not found. Install the Windows SDK packaging tools."
    }

    return $tool.FullName
}

function ConvertTo-XmlAttribute {
    param([Parameter(Mandatory = $true)][string] $Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function New-PackageAsset {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][int] $Width,
        [Parameter(Mandatory = $true)][int] $Height
    )

    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([System.Drawing.Color]::FromArgb(15, 18, 24))

        $side = [Math]::Max(1, [Math]::Floor([Math]::Min($Width, $Height) * 0.72))
        $left = [Math]::Floor(($Width - $side) / 2)
        $top = [Math]::Floor(($Height - $side) / 2)
        $radius = [Math]::Max(2, [Math]::Floor($side * 0.22))
        $pathShape = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $diameter = $radius * 2
            $pathShape.AddArc($left, $top, $diameter, $diameter, 180, 90)
            $pathShape.AddArc($left + $side - $diameter, $top, $diameter, $diameter, 270, 90)
            $pathShape.AddArc($left + $side - $diameter, $top + $side - $diameter, $diameter, $diameter, 0, 90)
            $pathShape.AddArc($left, $top + $side - $diameter, $diameter, $diameter, 90, 90)
            $pathShape.CloseFigure()
            $yellow = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(247, 185, 70))
            try { $graphics.FillPath($yellow, $pathShape) } finally { $yellow.Dispose() }
        }
        finally {
            $pathShape.Dispose()
        }

        $fontSize = [Math]::Max(6, [Math]::Floor($side * 0.34))
        $font = [System.Drawing.Font]::new('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $brush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(15, 18, 24))
        $format = [System.Drawing.StringFormat]::new()
        try {
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $graphics.DrawString('LV', $font, $brush, [System.Drawing.RectangleF]::new($left, $top, $side, $side), $format)
        }
        finally {
            $format.Dispose()
            $brush.Dispose()
            $font.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$parsedVersion = [version]::Parse($Version)
foreach ($component in @($parsedVersion.Major, $parsedVersion.Minor, $parsedVersion.Build, $parsedVersion.Revision)) {
    if ($component -gt 65535) {
        throw 'Each MSIX version component must be between 0 and 65535.'
    }
}

if ([string]::IsNullOrWhiteSpace($AppInstallerVersion)) {
    $AppInstallerVersion = $Version
}

if ($AppInstallerVersion -notmatch '^[1-9][0-9]*\.[0-9]+\.[0-9]+\.[0-9]+$') {
    throw 'AppInstallerVersion must use non-zero quad notation such as 1.0.0.0.'
}

$parsedAppInstallerVersion = [version]::Parse($AppInstallerVersion)
foreach ($component in @($parsedAppInstallerVersion.Major, $parsedAppInstallerVersion.Minor, $parsedAppInstallerVersion.Build, $parsedAppInstallerVersion.Revision)) {
    if ($component -gt 65535) {
        throw 'Each App Installer version component must be between 0 and 65535.'
    }
}

if (-not $AllowInsecureReleaseUri -and $ReleaseBaseUri.Scheme -ne 'https') {
    throw 'ReleaseBaseUri must use HTTPS. Use -AllowInsecureReleaseUri only for local packaging tests.'
}

if (-not $Unsigned -and [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
    throw 'Production packages must be signed. Pass -CertificateThumbprint or explicitly use -Unsigned for CI/local smoke tests.'
}

try {
    $null = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
}
catch {
    throw "Publisher must be a valid X.500 distinguished name: $Publisher"
}

if (-not $Unsigned) {
    $normalizedThumbprint = $CertificateThumbprint.Replace(' ', '').ToUpperInvariant()
    $matchingCertificates = @(
        Get-ChildItem -Path Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -eq $normalizedThumbprint }
    )
    if ($matchingCertificates.Count -ne 1) {
        throw 'CertificateThumbprint must resolve to exactly one certificate in CurrentUser/My or LocalMachine/My.'
    }

    $signingCertificate = $matchingCertificates[0]
    if (-not $signingCertificate.HasPrivateKey) {
        throw 'The signing certificate does not have an accessible private key.'
    }
    if ($signingCertificate.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or
        $signingCertificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
        throw 'The signing certificate is not currently valid.'
    }

    $expectedPublisher = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
    $actualPublisherBytes = [Convert]::ToBase64String($signingCertificate.SubjectName.RawData)
    $expectedPublisherBytes = [Convert]::ToBase64String($expectedPublisher.RawData)
    if ($actualPublisherBytes -ne $expectedPublisherBytes) {
        throw "The signing certificate subject must exactly match Publisher. Certificate subject: $($signingCertificate.Subject)"
    }

    $ekuExtension = $signingCertificate.Extensions |
        Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
        Select-Object -First 1
    if ($null -ne $ekuExtension) {
        $enhancedKeyUsage = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension
        $hasCodeSigningUsage = $enhancedKeyUsage.EnhancedKeyUsages |
            Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }
        if ($null -eq $hasCodeSigningUsage) {
            throw 'The signing certificate EKU does not allow code signing.'
        }
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $repositoryRoot 'artifacts\release'
$releaseRoot = Join-Path $artifactRoot "$Channel\$Version\$Runtime"
$stagingRoot = Join-Path $releaseRoot 'staging'
$packageRoot = Join-Path $stagingRoot 'package'
$assetsRoot = Join-Path $packageRoot 'Assets'

if (Test-Path $releaseRoot) {
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedReleaseRoot = [System.IO.Path]::GetFullPath($releaseRoot)
    if (-not $resolvedReleaseRoot.StartsWith($resolvedArtifactRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a release directory outside $resolvedArtifactRoot"
    }
    Remove-Item -LiteralPath $resolvedReleaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $assetsRoot -Force | Out-Null

$projectPath = Join-Path $repositoryRoot 'src\LexVerse.App\LexVerse.App.csproj'
$publishArguments = @(
    'publish', $projectPath,
    '--configuration', 'Release',
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $packageRoot,
    '-p:DebugSymbols=false',
    '-p:DebugType=None',
    '-p:PublishReadyToRun=true',
    "-p:Version=$Version",
    "-p:AssemblyVersion=$Version",
    "-p:FileVersion=$Version"
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

New-PackageAsset -Path (Join-Path $assetsRoot 'Square44x44Logo.png') -Width 44 -Height 44
New-PackageAsset -Path (Join-Path $assetsRoot 'Square150x150Logo.png') -Width 150 -Height 150
New-PackageAsset -Path (Join-Path $assetsRoot 'Wide310x150Logo.png') -Width 310 -Height 150
New-PackageAsset -Path (Join-Path $assetsRoot 'StoreLogo.png') -Width 50 -Height 50

$isBeta = $Channel -eq 'beta'
$packageName = if ($isBeta) { 'LexVerse.Desktop.Beta' } else { 'LexVerse.Desktop' }
$displayName = if ($isBeta) { 'LexVerse Beta' } else { 'LexVerse' }
$architecture = 'x64'
$escapedPackageName = ConvertTo-XmlAttribute $packageName
$escapedPublisher = ConvertTo-XmlAttribute $Publisher
$escapedPublisherName = ConvertTo-XmlAttribute $PublisherDisplayName
$escapedDisplayName = ConvertTo-XmlAttribute $displayName

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">
  <Identity Name="$escapedPackageName" Publisher="$escapedPublisher" Version="$Version" ProcessorArchitecture="$architecture" />
  <Properties>
    <DisplayName>$escapedDisplayName</DisplayName>
    <PublisherDisplayName>$escapedPublisherName</PublisherDisplayName>
    <Description>Realtime screen translation for Windows</Description>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>
  <Applications>
    <Application Id="LexVerse" Executable="LexVerse.App.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="$escapedDisplayName"
        Description="Realtime screen translation for Windows"
        BackgroundColor="#0F1218"
        Square44x44Logo="Assets\Square44x44Logo.png"
        Square150x150Logo="Assets\Square150x150Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

$manifestPath = Join-Path $packageRoot 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

$makeAppx = Get-WindowsSdkTool -Name 'MakeAppx.exe'
$packageFileName = "LexVerse-$Channel-$Version-$architecture.msix"
$packagePath = Join-Path $releaseRoot $packageFileName
& $makeAppx pack /d $packageRoot /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

if (-not $Unsigned) {
    $signTool = Get-WindowsSdkTool -Name 'SignTool.exe'
    & $signTool sign /fd SHA256 /sha1 $normalizedThumbprint /tr $TimestampUri.AbsoluteUri /td SHA256 $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }

    & $signTool verify /pa /v $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw "Package signature verification failed with exit code $LASTEXITCODE."
    }
}

$baseUri = $ReleaseBaseUri.AbsoluteUri.TrimEnd('/')
$channelBaseUri = "$baseUri/$Channel"
$appInstallerFileName = "LexVerse-$Channel.appinstaller"
$appInstallerUri = "$channelBaseUri/$appInstallerFileName"
$packageUri = "$channelBaseUri/$packageFileName"
$escapedAppInstallerUri = ConvertTo-XmlAttribute $appInstallerUri
$escapedPackageUri = ConvertTo-XmlAttribute $packageUri

$appInstaller = @"
<?xml version="1.0" encoding="utf-8"?>
<s3:AppInstaller xmlns:s3="http://schemas.microsoft.com/appx/appinstaller/2018" Version="$AppInstallerVersion" Uri="$escapedAppInstallerUri">
  <s3:MainPackage Name="$escapedPackageName" Publisher="$escapedPublisher" Version="$Version" ProcessorArchitecture="$architecture" Uri="$escapedPackageUri" />
  <s3:UpdateSettings>
    <s3:OnLaunch HoursBetweenUpdateChecks="0" ShowPrompt="true" UpdateBlocksActivation="false" />
    <s3:AutomaticBackgroundTask />
    <s3:ForceUpdateFromAnyVersion>true</s3:ForceUpdateFromAnyVersion>
  </s3:UpdateSettings>
</s3:AppInstaller>
"@

$appInstallerPath = Join-Path $releaseRoot $appInstallerFileName
[System.IO.File]::WriteAllText($appInstallerPath, $appInstaller, [System.Text.UTF8Encoding]::new($false))

$packageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash.ToLowerInvariant()
$hashPath = "$packagePath.sha256"
[System.IO.File]::WriteAllText($hashPath, "$packageHash  $packageFileName`n", [System.Text.UTF8Encoding]::new($false))

Remove-Item -LiteralPath $stagingRoot -Recurse -Force

[pscustomobject]@{
    Channel = $Channel
    Version = $Version
    AppInstallerVersion = $AppInstallerVersion
    Signed = -not $Unsigned
    Package = $packagePath
    AppInstaller = $appInstallerPath
    Sha256 = $packageHash
} | Format-List
