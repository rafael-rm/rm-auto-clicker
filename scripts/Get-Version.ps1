param(
    [string]$Tag = '',
    [string]$VersionFile = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props')
)

$ErrorActionPreference = 'Stop'
$properties = [xml](Get-Content -LiteralPath $VersionFile -Raw)
$versions = $properties.SelectNodes('/Project/PropertyGroup/Version')
if ($versions.Count -ne 1) {
    throw 'Define exactly one version in Directory.Build.props.'
}

$version = $versions[0].InnerText.Trim()
$pattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?$'
if ($version -cnotmatch $pattern) {
    throw "Invalid version: '$version'. Use MAJOR.MINOR.PATCH or MAJOR.MINOR.PATCH-prerelease without build metadata."
}
if ($Tag -and $Tag -cne "v$version") {
    throw "Tag '$Tag' does not match source version 'v$version'."
}

[pscustomobject]@{
    Version = $version
    Tag = "v$version"
    IsPrerelease = $version.Contains('-')
    AssetName = "RMAutoClicker-$version-win-x64.exe"
}
