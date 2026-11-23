param([ValidatePattern('^[0-9A-Za-z.-]+$')][string]$Version='1.6.0-W-preview10')
$ErrorActionPreference='Stop'
$windowsRoot=Split-Path -Parent $PSScriptRoot
$repoRoot=Split-Path -Parent $windowsRoot
$sdk=Join-Path $windowsRoot '.tools/dotnet/dotnet.exe'
if(!(Test-Path -LiteralPath $sdk)){$sdk=(Get-Command dotnet -ErrorAction Stop).Source}
$sourceCommit=(& git -C $repoRoot rev-parse HEAD).Trim()
& git -C $repoRoot diff --quiet HEAD -- windows/AwesomeInterview.NativeDemo windows/AwesomeInterview.NativeChecks
if($LASTEXITCODE -ne 0){throw 'Commit native sources before packaging.'}
$artifacts=Join-Path $windowsRoot 'artifacts'
$package=Join-Path $artifacts ('AwesomeInterview-NativeDemo-'+$Version+'-win-x64')
$archive=$package+'.zip'
if(Test-Path -LiteralPath $archive){throw ('Archive already exists: '+$archive)}
New-Item -ItemType Directory -Force -Path $package | Out-Null
$app=Join-Path $package 'app'
& $sdk publish (Join-Path $windowsRoot 'AwesomeInterview.NativeDemo/AwesomeInterview.NativeDemo.csproj') -c Release -r win-x64 --self-contained true '-p:DebugType=none' -o $app --nologo -v:q
if($LASTEXITCODE -ne 0){throw 'Native publish failed.'}
foreach($name in @('Install.cmd','Uninstall.cmd')){Copy-Item -LiteralPath (Join-Path $windowsRoot ('installer/'+$name)) -Destination (Join-Path $package $name)}
Copy-Item -LiteralPath (Join-Path $windowsRoot 'NativeDemo-README.md') -Destination (Join-Path $package 'README.md')
[ordered]@{version=$Version;source_commit=$sourceCommit;built_at_utc=[DateTime]::UtcNow.ToString('o');runtime='win-x64';self_contained=$true;ui_acceptance='pending-device-validation';deployment='no-deployment-performed';backend_requirement='native workspace and idea-sync APIs from the same source revision'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'build-manifest.json') -Encoding UTF8
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $archive -CompressionLevel Optimal
$digest=Get-FileHash -LiteralPath $archive -Algorithm SHA256
($digest.Hash+'  '+[IO.Path]::GetFileName($archive)) | Set-Content -LiteralPath ($archive+'.sha256') -Encoding ASCII
Write-Output ('Package: '+$archive)
Write-Output ('SHA256: '+$digest.Hash)
