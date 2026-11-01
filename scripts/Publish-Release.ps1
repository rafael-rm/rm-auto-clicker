param(
    [Parameter(Mandatory)]
    [string]$Tag,
    [Parameter(Mandatory)]
    [string]$Directory,
    [Parameter(Mandatory)]
    [string]$ExpectedCommit,
    [Parameter(Mandatory)]
    [string]$ExpectedSHA256,
    [Parameter(Mandatory)]
    [string]$Repository
)

$ErrorActionPreference = 'Stop'
$release = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -Tag $Tag
$package = & (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -Directory $Directory -Tag $Tag -ExpectedSHA256 $ExpectedSHA256
$sourceScript = Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1'
& $sourceScript -Tag $Tag -ExpectedCommit $ExpectedCommit -Repository $Repository | Out-Null

$arguments = @('release', 'create', $Tag, $package.Executable, $package.Checksum,
    '--repo', $Repository, '--draft', '--verify-tag', '--generate-notes', '--latest=false',
    '--title', "RM Auto Clicker $($release.Version)")
if ($release.IsPrerelease) {
    $arguments += '--prerelease'
}
gh @arguments | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not prepare the draft release. Existing releases are not overwritten.'
}

# The REST lookup by tag only finds published releases; the CLI also finds drafts.
$response = gh release view $Tag --repo $Repository --json 'databaseId,tagName,isDraft,isPrerelease'
if ($LASTEXITCODE -ne 0) {
    throw 'Could not identify the draft release. Review the draft before retrying publication.'
}
try {
    $draft = ($response -join "`n") | ConvertFrom-Json -ErrorAction Stop
} catch {
    throw 'GitHub returned an invalid response for the draft release.'
}
$releaseId = 0L
if ($draft -is [array] -or $draft.tagName -cne $Tag -or $draft.isDraft -isnot [bool] -or -not $draft.isDraft -or
    $draft.isPrerelease -isnot [bool] -or $draft.isPrerelease -ne $release.IsPrerelease -or
    -not [long]::TryParse([string]$draft.databaseId, [ref]$releaseId) -or $releaseId -le 0) {
    throw 'GitHub did not return the expected draft release.'
}

& $sourceScript -Tag $Tag -ExpectedCommit $ExpectedCommit -Repository $Repository | Out-Null
# Let GitHub select latest by date and semantic version without a client-side promotion race.
$latestPolicy = if ($release.IsPrerelease) { 'false' } else { 'legacy' }
gh api --hostname github.com --method PATCH "repos/$Repository/releases/$releaseId" `
    --field 'draft=false' --raw-field "make_latest=$latestPolicy" | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not publish the validated draft. Review the draft before retrying publication.'
}

[pscustomobject]@{ Tag = $Tag; ReleaseId = $releaseId; LatestPolicy = $latestPolicy; SHA256 = $package.SHA256 }
