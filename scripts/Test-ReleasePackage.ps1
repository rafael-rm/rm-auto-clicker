param(
    [Parameter(Mandatory)]
    [string]$Directory,
    [string]$Tag = '',
    [string]$ExpectedSHA256 = ''
)

$ErrorActionPreference = 'Stop'
if ($PSBoundParameters.ContainsKey('ExpectedSHA256') -and $ExpectedSHA256 -notmatch '^[0-9a-f]{64}$') {
    throw 'The expected SHA-256 hash from CI is invalid.'
}
$release = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -Tag $Tag
$directoryPath = (Resolve-Path -LiteralPath $Directory).ProviderPath
$entries = @(Get-ChildItem -Force -LiteralPath $directoryPath)
$expectedNames = @($release.AssetName, 'SHA256SUMS.txt')
if ($entries.Count -ne 2 -or @($entries | Where-Object {
    $_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $expectedNames -cnotcontains $_.Name
}).Count -gt 0) {
    throw 'The package must contain only the versioned executable and SHA256SUMS.txt, with no subdirectories or links.'
}

$executable = Join-Path $directoryPath $release.AssetName
if ((Get-Item -LiteralPath $executable).Length -eq 0) {
    throw 'The executable is empty.'
}

$checksum = Join-Path $directoryPath 'SHA256SUMS.txt'
$lines = @(Get-Content -LiteralPath $checksum)
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($lines.Count -ne 1 -or $lines[0] -cne "$hash *$($release.AssetName)") {
    throw 'The checksum does not match the expected executable.'
}
if ($PSBoundParameters.ContainsKey('ExpectedSHA256') -and $hash -cne $ExpectedSHA256.ToLowerInvariant()) {
    throw 'The executable does not match the SHA-256 hash recorded by CI.'
}

[pscustomobject]@{
    Version = $release.Version
    Executable = $executable
    Checksum = $checksum
    SHA256 = $hash
}
