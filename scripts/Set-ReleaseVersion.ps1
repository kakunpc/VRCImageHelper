param(
    [Parameter(Mandatory)][string]$Tag,
    [string]$Root = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'

# MSI ProductVersion supports three numeric fields: byte, byte, ushort.
if ($Tag -cnotmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Use a release tag in the form vMAJOR.MINOR.PATCH (no prerelease suffix).'
}
$version = $Tag.Substring(1)
$parts = $version.Split('.')
if ([long]$parts[0] -gt 255 -or [long]$parts[1] -gt 255 -or [long]$parts[2] -gt 65535) {
    throw 'MSI version limits are MAJOR <= 255, MINOR <= 255, PATCH <= 65535.'
}
$projectPath = Join-Path $Root 'VRCImageHelper/VRCImageHelper.csproj'
$setupPath = Join-Path $Root 'Setup/Setup.vdproj'
$project = [IO.File]::ReadAllText($projectPath)
$setup = [IO.File]::ReadAllText($setupPath)
function Replace-One([string]$Text, [string]$Pattern, [string]$Value) {
    if ([regex]::Matches($Text, $Pattern).Count -ne 1) { throw "Expected one match: $Pattern" }
    return [regex]::Replace($Text, $Pattern, [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $Value })
}
$project = Replace-One $project '<VersionPrefix>[^<]+</VersionPrefix>' "<VersionPrefix>$version</VersionPrefix>"
$setup = Replace-One $setup '"ProductVersion" = "8:[^"]+"' ('"ProductVersion" = "8:' + $version + '"')
# A version has a stable ProductCode across retries; each MSI build gets a new PackageCode.
# UpgradeCode stays unchanged so installed older versions can be detected.
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("VRCImageHelper:$version"))
$productCode = [Guid]::new([byte[]]$hash[0..15]).ToString('B').ToUpperInvariant()
$packageCode = [Guid]::NewGuid().ToString('B').ToUpperInvariant()
$setup = Replace-One $setup '"ProductCode" = "8:\{[^}]+\}"' ('"ProductCode" = "8:' + $productCode + '"')
$setup = Replace-One $setup '"PackageCode" = "8:\{[^}]+\}"' ('"PackageCode" = "8:' + $packageCode + '"')
# The old post-build hook hardcodes a developer's Windows SDK path. The CI
# build applies the equivalent per-user package flag through the MSI API.
$setup = Replace-One $setup '(?m)^\s*"PostBuildEvent" = .*?$' '        "PostBuildEvent" = "8:"'
$setup = $setup.Replace('obj\\Debug\\net10.0', 'obj\\Release\\net10.0')
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($setupPath, $setup, [Text.UTF8Encoding]::new($false))
Write-Output $version
