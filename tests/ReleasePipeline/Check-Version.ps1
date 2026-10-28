$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('VRCImageHelper-release-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory "$fixture/Setup", "$fixture/VRCImageHelper" | Out-Null
function Check([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Property([string]$Text, [string]$Name) {
    if ($Name.EndsWith('Code')) {
        return [regex]::Match($Text, ('"' + $Name + '" = "8:(\{[^}]+\})"')).Groups[1].Value
    }
    return [regex]::Match($Text, ('"' + $Name + '" = "8:([^"]+)"')).Groups[1].Value
}
try {
    Copy-Item "$root/Setup/Setup.vdproj" "$fixture/Setup"
    Copy-Item "$root/VRCImageHelper/VRCImageHelper.csproj" "$fixture/VRCImageHelper"
    $original = [IO.File]::ReadAllText("$fixture/Setup/Setup.vdproj")
    foreach ($tag in @('0.9.10', 'v01.2.3', 'v1.2', 'v1.2.3-beta.1', 'v256.0.0', 'v0.256.0', 'v0.0.65536', 'v1.2.3/unsafe')) {
        $rejected = $false
        try { & "$root/scripts/Set-ReleaseVersion.ps1" -Tag $tag -Root $fixture | Out-Null } catch { $rejected = $true }
        Check $rejected "Invalid tag accepted: $tag"
        Check ([IO.File]::ReadAllText("$fixture/Setup/Setup.vdproj") -ceq $original) 'Invalid tag changed installer.'
    }
    $version = & "$root/scripts/Set-ReleaseVersion.ps1" -Tag v0.9.10 -Root $fixture
    Check ($version -ceq '0.9.10') 'Unexpected version output.'
    $first = [IO.File]::ReadAllText("$fixture/Setup/Setup.vdproj")
    [xml]$project = Get-Content "$fixture/VRCImageHelper/VRCImageHelper.csproj" -Raw
    Check ($project.SelectSingleNode('/Project/PropertyGroup/VersionPrefix').InnerText -eq '0.9.10') 'App version mismatch.'
    Check ((Property $first 'ProductVersion') -eq '0.9.10') 'MSI version mismatch.'
    Check ((Property $first 'UpgradeCode') -eq (Property $original 'UpgradeCode')) 'UpgradeCode changed.'
    Check ((Property $first 'ProductCode') -ne (Property $original 'ProductCode')) 'New version reused old ProductCode.'
    Check ($first.Contains('obj\\Release\\net10.0')) 'Release apphost path missing.'
    Check ($first.Contains('"PostBuildEvent" = "8:"')) 'Machine-specific hook still present.'
    & "$root/scripts/Set-ReleaseVersion.ps1" -Tag v0.9.10 -Root $fixture | Out-Null
    $second = [IO.File]::ReadAllText("$fixture/Setup/Setup.vdproj")
    Check ((Property $first 'ProductCode') -eq (Property $second 'ProductCode')) 'Retry changed ProductCode.'
    Check ((Property $first 'PackageCode') -ne (Property $second 'PackageCode')) 'Retry reused PackageCode.'
    & "$root/scripts/Set-ReleaseVersion.ps1" -Tag v0.9.11 -Root $fixture | Out-Null
    $third = [IO.File]::ReadAllText("$fixture/Setup/Setup.vdproj")
    Check ((Property $third 'ProductCode') -ne (Property $second 'ProductCode')) 'Different versions share ProductCode.'
    & "$root/scripts/Set-ReleaseVersion.ps1" -Tag v255.255.65535 -Root $fixture | Out-Null
    Check ((Property ([IO.File]::ReadAllText("$fixture/Setup/Setup.vdproj")) 'ProductVersion') -eq '255.255.65535') 'MSI maximum version rejected.'
    # Test the native summary update on an empty disposable MSI, without any
    # install operation or custom action.
    $msi = Join-Path $fixture 'summary-test.msi'
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($msi, 3)
    $database.Commit()
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
    & "$root/scripts/Set-MsiPackageFlags.ps1" -Path $msi
    $database = $installer.OpenDatabase($msi, 0)
    $summary = $database.GetType().InvokeMember('SummaryInformation', [Reflection.BindingFlags]::GetProperty, $null, $database, @([int]0))
    $flags = $summary.GetType().InvokeMember('Property', [Reflection.BindingFlags]::GetProperty, $null, $summary, @([int]15))
    Check ($flags -eq 10) 'Per-user package flags did not persist.'
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
    [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
    Write-Host 'PASS release version validation, app/MSI alignment, upgrade identity and rerun identities (isolated files only).'
} finally {
    # This is the GUID-named directory created above, never a supplied path.
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
