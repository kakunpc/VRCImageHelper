param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products '*' -version '[18.0,19.0)' -property installationPath
if (!$vs) { throw 'Visual Studio 2026 with Installer Projects is required (windows-2025-vs2026 runner).' }
$devenv = Join-Path $vs 'Common7/IDE/devenv.com'
$workaround = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/VSI/DisableOutOfProcBuild/DisableOutOfProcBuild.exe'
if (!(Test-Path $workaround)) { throw 'Visual Studio Installer Projects extension is missing.' }
Push-Location $vs
try { Invoke-Checked $workaround @() } finally { Pop-Location }

Invoke-Checked 'dotnet' @('publish', 'VRCImageHelper/VRCImageHelper.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false')
$publish = Join-Path $root 'VRCImageHelper/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish'
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $publish 'VRCImageHelper.dll')).ProductVersion.Split('+')[0]
if ($appVersion -ne $Version) { throw "App version mismatch: $appVersion != $Version" }
New-Item -ItemType Directory -Force 'artifacts/logs', 'artifacts/installer' | Out-Null
Invoke-Checked $devenv @('VRCImageHelper.sln', '/Build', 'Release|Any CPU', '/Project', 'Setup', '/ProjectConfig', 'Release', '/Out', (Join-Path $root 'artifacts/logs/installer.log'))

$msi = Join-Path $root 'Setup/Release/Setup.msi'
$bootstrapper = Join-Path $root 'Setup/Release/setup.exe'
foreach ($file in @($msi, $bootstrapper)) {
    if (!(Test-Path $file) -or (Get-Item $file).Length -eq 0) { throw "Missing installer output: $file" }
}
# Inspect the MSI database without running install/custom actions.
$installer = New-Object -ComObject WindowsInstaller.Installer
function Get-ComProperty($Object, [string]$Name, [object[]]$Arguments) {
    return $Object.GetType().InvokeMember($Name, [Reflection.BindingFlags]::GetProperty, $null, $Object, $Arguments)
}
# Preserve the existing MsiInfo.exe -w 10 hook without depending on one SDK's
# installation path: compressed package (2) + per-user/LUA package (8).
$summary = Get-ComProperty $installer 'SummaryInformation' @($msi, [int]1)
$summary.GetType().InvokeMember('Property', [Reflection.BindingFlags]::SetProperty, $null, $summary, @([int]15, [int]10)) | Out-Null
$summary.Persist()
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
$database = $installer.OpenDatabase($msi, 0)
$summary = Get-ComProperty $database 'SummaryInformation' @([int]0)
function Read-MsiProperty([string]$Name) {
    $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'")
    $view.Execute()
    $record = $view.Fetch()
    if (!$record) { throw "Missing MSI property: $Name" }
    return Get-ComProperty $record 'StringData' @([int]1)
}
if ((Read-MsiProperty 'ProductVersion') -ne $Version) { throw 'MSI version mismatch.' }
if ((Read-MsiProperty 'UpgradeCode') -ne '{A68DD4DB-3CB4-400D-AB4C-9EC599156F0D}') { throw 'MSI UpgradeCode changed.' }
if ((Get-ComProperty $summary 'Property' @([int]7)) -notmatch '^x64;') { throw 'MSI must target x64.' }
$flags = Get-ComProperty $summary 'Property' @([int]15)
if ($flags -ne 10) { throw "MSI per-user package flags mismatch: $flags (expected 10)." }
$view = $database.OpenView('SELECT `FileName` FROM `File`')
$view.Execute()
$files = @(); while ($record = $view.Fetch()) { $files += ((Get-ComProperty $record 'StringData' @([int]1)).Split('|')[-1]) }
foreach ($required in @('VRCImageHelper.exe', 'VRCImageHelper.dll', 'VRCImageHelper.runtimeconfig.json', 'VRCImageHelper.deps.json')) {
    if ($required -notin $files) { throw "Publish Items missing from MSI: $required" }
}
# Keep setup.exe and Setup.msi together: the bootstrapper refers to this MSI name.
Copy-Item -LiteralPath $msi, $bootstrapper -Destination 'artifacts/installer'
$zip = "artifacts/VRCImageHelper-$Version-win-x64-installer.zip"
Compress-Archive -Path 'artifacts/installer/*' -DestinationPath $zip -Force
$checksum = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + (Split-Path $zip -Leaf)
[IO.File]::WriteAllText((Join-Path $root "$zip.sha256"), "$checksum`n", [Text.UTF8Encoding]::new($false))
