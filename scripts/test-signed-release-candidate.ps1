[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ReleaseDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('stable', 'beta')]
    [string] $Channel,

    [Parameter(Mandatory = $true)]
    [version] $Version,

    [Parameter(Mandatory = $true)]
    [version] $AppInstallerVersion,

    [Parameter(Mandatory = $true)]
    [uri] $ReleaseBaseUri,

    [Parameter(Mandatory = $true)]
    [string] $Publisher
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$releaseRoot = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$packageName = "LexVerse-$Channel-$Version-x64.msix"
$packagePath = Join-Path $releaseRoot $packageName
$checksumPath = "$packagePath.sha256"
$appInstallerPath = Join-Path $releaseRoot "LexVerse-$Channel.appinstaller"

foreach ($path in @($packagePath, $checksumPath, $appInstallerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release artifact is missing: $path"
    }
}

if ((Get-Item -LiteralPath $packagePath).Length -lt 1MB) {
    throw 'The MSIX is unexpectedly small.'
}

$expectedHash = ((Get-Content -LiteralPath $checksumPath -Raw).Split(' ')[0]).Trim().ToLowerInvariant()
$actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash.ToLowerInvariant()
if ($expectedHash -ne $actualHash) {
    throw 'The signed MSIX SHA-256 does not match its checksum file.'
}

$signature = Get-AuthenticodeSignature -LiteralPath $packagePath
if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "The MSIX Authenticode signature is not valid: $($signature.StatusMessage)"
}

$expectedPublisher = [System.Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
if ([Convert]::ToBase64String($signature.SignerCertificate.SubjectName.RawData) -ne
    [Convert]::ToBase64String($expectedPublisher.RawData)) {
    throw 'The MSIX signer subject does not match the configured Publisher.'
}

[xml]$appInstaller = Get-Content -LiteralPath $appInstallerPath -Raw
$root = $appInstaller.AppInstaller
if ([version]$root.Version -ne $AppInstallerVersion) {
    throw 'The App Installer channel version does not match the requested version.'
}
if ([version]$root.MainPackage.Version -ne $Version) {
    throw 'The App Installer package version does not match the requested package.'
}
if ($root.MainPackage.Publisher -ne $Publisher) {
    throw 'The App Installer publisher does not match the signed package publisher.'
}

$expectedBase = $ReleaseBaseUri.AbsoluteUri.TrimEnd('/')
$expectedManifestUri = "$expectedBase/$Channel/LexVerse-$Channel.appinstaller"
$expectedPackageUri = "$expectedBase/$Channel/$packageName"
if ($root.Uri -ne $expectedManifestUri -or $root.MainPackage.Uri -ne $expectedPackageUri) {
    throw 'The App Installer URLs do not match the release channel layout.'
}
if (([uri]$root.Uri).Scheme -ne 'https') {
    throw 'The App Installer channel pointer must use HTTPS.'
}
if ($null -eq $signature.TimeStamperCertificate) {
    throw 'The MSIX signature does not contain a trusted timestamp certificate.'
}
if (Test-Path -LiteralPath (Join-Path $releaseRoot 'staging')) {
    throw 'Packaging staging files must not be retained in a release candidate.'
}

[pscustomobject]@{
    Channel = $Channel
    Version = $Version.ToString()
    AppInstallerVersion = $AppInstallerVersion.ToString()
    Package = $packagePath
    Sha256 = $actualHash
    Signer = $signature.SignerCertificate.Subject
    TimestampCertificate = $signature.TimeStamperCertificate.Subject
    AppInstaller = $appInstallerPath
} | Format-List
