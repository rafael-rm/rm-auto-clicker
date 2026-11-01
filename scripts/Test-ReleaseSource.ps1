param(
    [Parameter(Mandatory)]
    [string]$Tag,
    [Parameter(Mandatory)]
    [AllowEmptyString()]
    [string]$ExpectedCommit,
    [Parameter(Mandatory)]
    [string]$Repository,
    [string]$RepositoryDirectory = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
if ($ExpectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'The build commit SHA is invalid.'
}
if ($Repository -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+$' -or $Repository.Split('/')[-1] -in @('.', '..')) {
    throw 'The repository must use the owner/name format.'
}
$release = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -Tag $Tag
$localCommit = git -C $RepositoryDirectory rev-parse --verify 'HEAD^{commit}' 2>$null
if ($LASTEXITCODE -ne 0 -or $localCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not identify the release checkout commit.'
}
if ($localCommit -ine $ExpectedCommit) {
    throw 'The release checkout does not match the build commit.'
}

function Read-GitHubObject {
    param([string]$Endpoint)

    $response = gh api --hostname github.com --method GET $Endpoint 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not query the release reference on GitHub.'
    }
    try {
        return ($response -join "`n") | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw 'GitHub returned an invalid response for the release reference.'
    }
}

$reference = Read-GitHubObject "repos/$Repository/git/ref/tags/$($release.Tag)"
if ($reference -is [array] -or $reference.ref -cne "refs/tags/$($release.Tag)") {
    throw 'GitHub did not return the exact release tag reference.'
}
$target = $reference.object
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
while ($true) {
    if ($target.sha -notmatch '^[0-9a-f]{40}$' -or $target.type -cnotin @('commit', 'tag')) {
        throw 'Invalid Git object in the release reference.'
    }
    if ($target.type -ceq 'commit') {
        break
    }
    if ($visited.Count -ge 16 -or -not $visited.Add($target.sha)) {
        throw 'The annotated tag chain contains a cycle or exceeds the allowed depth.'
    }
    $annotation = Read-GitHubObject "repos/$Repository/git/tags/$($target.sha)"
    if ($annotation.sha -ine $target.sha) {
        throw 'GitHub returned an inconsistent annotated tag.'
    }
    $target = $annotation.object
}
if ($target.sha -ine $ExpectedCommit) {
    throw "Tag '$($release.Tag)' does not point to the build commit. Publication canceled."
}

[pscustomobject]@{
    Tag = $release.Tag
    CommitSha = $target.sha.ToLowerInvariant()
    Repository = $Repository
}
