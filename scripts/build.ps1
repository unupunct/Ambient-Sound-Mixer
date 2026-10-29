<#
.SYNOPSIS
  Repeatable release build: restore, build, test, publish (self-contained x64), portable ZIP, installer, SHA-256 checksums.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build.ps1
  powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -SkipTests -SkipInstaller
#>
param(
    [switch]$SkipTests,
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Force $publish | Out-Null

Write-Host "== Ambient Sound Mixer $version =="
dotnet restore AmbientSoundMixer.sln
if ($LASTEXITCODE) { throw 'restore failed' }
dotnet build AmbientSoundMixer.sln -c Release --no-restore
if ($LASTEXITCODE) { throw 'build failed' }

if (-not $SkipTests) {
    dotnet test AmbientSoundMixer.sln -c Release --no-build
    if ($LASTEXITCODE) { throw 'tests failed' }
}

dotnet publish src/AmbientSoundMixer.App/AmbientSoundMixer.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -o $publish
if ($LASTEXITCODE) { throw 'publish failed' }
Copy-Item LICENSE, THIRD-PARTY-NOTICES.md -Destination $publish

$zip = Join-Path $artifacts "AmbientSoundMixer-$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command iscc -ErrorAction SilentlyContinue).Source,
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) { throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or pass -SkipInstaller.' }
    & $iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$artifacts" installer\AmbientSoundMixer.iss
    if ($LASTEXITCODE) { throw 'installer build failed' }
}

$sums = Get-ChildItem $artifacts -File | Where-Object { $_.Extension -in '.exe', '.zip' } | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
}
$sums | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
Write-Host "`nArtifacts:"
Get-ChildItem $artifacts -File | ForEach-Object { "{0,12:N0}  {1}" -f $_.Length, $_.Name }
Write-Host "`nSHA-256:"
$sums
