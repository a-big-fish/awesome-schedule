#Requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Signed')]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $ReleaseNotes,

    [Parameter(Mandatory)]
    [string] $OutputDir,

    [string] $PreviousReleaseDir,

    [Parameter(Mandatory, ParameterSetName = 'Signed')]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string] $CertificateThumbprint,

    [Parameter(Mandatory, ParameterSetName = 'LocalTest')]
    [switch] $UnsignedTest,

    [uri] $FeedUrl = 'https://schedule.hunaochufang.xyz/download/windows/updates/'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$windowsRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $windowsRoot AwesomeInterview.NativeDemo/AwesomeInterview.NativeDemo.csproj
$packageId = 'AwesomeInterviewWindows'
$feedName = 'releases.win.json'
$dotnet = Join-Path $windowsRoot .tools/dotnet/dotnet.exe
$vpk = Join-Path $windowsRoot .tools/velopack/vpk.exe
if (!(Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
if (!(Test-Path -LiteralPath $vpk)) { $vpk = (Get-Command vpk -ErrorAction Stop).Source }
if (((& $vpk --help) -join ' ') -notmatch 'Velopack CLI 1[.]2[.]161,') {
    throw 'Install Velopack CLI 1.2.161 to match the client SDK.'
}
if (!$FeedUrl.IsAbsoluteUri -or $FeedUrl.Scheme -ne 'https' -or $FeedUrl.UserInfo -or
    $FeedUrl.Query -or $FeedUrl.Fragment -or !$FeedUrl.AbsolutePath.EndsWith('/')) {
    throw 'FeedUrl must be an absolute HTTPS directory URL ending in /.'
}

$notesPath = (Resolve-Path -LiteralPath $ReleaseNotes).Path
$notes = @(Get-Content -LiteralPath $notesPath -Encoding utf8 | ForEach-Object {
    $_.Trim() -replace '^(?:#{1,6}|[-*])\s+', ''
} | Where-Object { $_ } | Select-Object -First 12)
$priorAssets = @()
if ($PreviousReleaseDir) {
    $previous = (Resolve-Path -LiteralPath $PreviousReleaseDir).Path
    $priorAssets = @(Get-ChildItem -LiteralPath $previous -File | Where-Object {
        $_.Name -match '^AwesomeInterviewWindows-[0-9A-Za-z.-]+-(full|delta)\.nupkg$' -or
        $_.Name -in @('assets.win.json', $feedName)
    })
}
if ($priorAssets.Name -contains "$packageId-$Version-full.nupkg") {
    throw 'This version already exists in PreviousReleaseDir. Increase Version.'
}
if (Test-Path -LiteralPath $OutputDir) { throw 'OutputDir must be a new staging directory.' }

$privateKey = $null
try {
    $publicKey = ''
    $signArgs = @()
    if (!$UnsignedTest) {
        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
        $privateKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
        if (!$privateKey -or $certificate.NotAfter -lt (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
            throw 'A current Authenticode certificate with an accessible RSA private key is required.'
        }
        $verificationKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
        try { $publicKey = [Convert]::ToBase64String($verificationKey.ExportSubjectPublicKeyInfo()) }
        finally { $verificationKey.Dispose() }
        $signArgs = @('--signParams', "/sha1 $CertificateThumbprint /s My /fd SHA256 /tr http://timestamp.digicert.com /td SHA256")
    }

    $staging = (New-Item -ItemType Directory -Path $OutputDir).FullName
    $publish = Join-Path $staging published-app
    $release = (New-Item -ItemType Directory -Path (Join-Path $staging release)).FullName
    foreach ($asset in $priorAssets) { Copy-Item -LiteralPath $asset.FullName -Destination $release }
    & $dotnet publish $project -c Release -r win-x64 --self-contained true -o $publish --nologo -v:q `
        "-p:Version=$Version" "-p:WindowsUpdatePublicKey=$publicKey" "-p:WindowsUpdateFeed=$($FeedUrl.AbsoluteUri)" '-p:DebugType=none'
    & $vpk pack --outputDir $release --packId $packageId --packVersion $Version --packDir $publish `
        --packTitle 'Awesome Interview' --mainExe AwesomeInterview.NativeDemo.exe --channel win --runtime win-x64 `
        --instLocation PerUser --noPortable --releaseNotes $notesPath @signArgs

    $setup = Join-Path $release "$packageId-win-Setup.exe"
    $manifest = Join-Path $release $feedName
    $installerName = "AwesomeInterview-Windows-$Version-x64-Setup.exe"
    $installer = Join-Path $release $installerName
    if (!$UnsignedTest) {
        $signature = Get-AuthenticodeSignature -LiteralPath $setup
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint) {
            throw 'The installer did not pass Authenticode verification with the selected certificate.'
        }
        $manifestBytes = [IO.File]::ReadAllBytes($manifest)
        $proof = $privateKey.SignData($manifestBytes, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        [IO.File]::WriteAllBytes("$manifest.sig", $proof)
    }
    Move-Item -LiteralPath $setup -Destination $installer
    $sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    if (!$UnsignedTest) {
        [ordered]@{ version = $Version; filename = $installerName; sha256 = $sha256; notes = $notes } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $release release.json) -Encoding utf8NoBOM
    }

    Write-Output "Release staging: $release"
    Write-Output "Installer SHA-256: $sha256"
    if ($UnsignedTest) { Write-Output 'Local test build. No signed update feed or public download catalog was produced.' }
}
finally {
    if ($privateKey) { $privateKey.Dispose() }
}
