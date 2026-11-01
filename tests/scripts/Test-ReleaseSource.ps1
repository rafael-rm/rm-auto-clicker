$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourceScript = Join-Path $repositoryRoot 'scripts/Test-ReleaseSource.ps1'
$release = & (Join-Path $repositoryRoot 'scripts/Get-Version.ps1')
$commit = git -C $repositoryRoot rev-parse --verify 'HEAD^{commit}'
if ($LASTEXITCODE -ne 0) {
    throw 'The tests require a valid Git checkout.'
}
$otherCommit = 'f' * 40
$annotationSha = 'a' * 40
$nestedSha = 'b' * 40
$referenceName = "refs/tags/$($release.Tag)"
$endpoint = "repos/owner/repo/git/ref/tags/$($release.Tag)"
$script:checks = 0
$githubState = [pscustomobject]@{ Responses = $null; Calls = $null }

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
    $script:checks++
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try {
        & $Action | Out-Null
    } catch {
        if ($_.Exception.Message -notlike $ExpectedMessage) {
            throw "Unexpected failure (expected: $ExpectedMessage): $($_.Exception.Message)"
        }
        $script:checks++
        return
    }
    throw "The operation should have failed: $ExpectedMessage"
}

function New-GitHubResponse {
    param([object]$Payload, [int]$ExitCode = 0)
    [pscustomobject]@{ Body = ($Payload | ConvertTo-Json -Depth 8 -Compress); ExitCode = $ExitCode }
}

function Set-GitHubResponses {
    param([object[]]$Responses)
    $script:githubResponses = [Collections.Generic.Queue[object]]::new()
    $script:githubCalls = [Collections.Generic.List[string]]::new()
    $githubState.Responses = $script:githubResponses
    $githubState.Calls = $script:githubCalls
    foreach ($response in $Responses) {
        $script:githubResponses.Enqueue($response)
    }
}

function gh {
    $arguments = @($args)
    if ($arguments.Count -ne 6 -or ($arguments[0..4] -join ' ') -cne 'api --hostname github.com --method GET') {
        throw 'The test only allows GET requests to the GitHub API.'
    }
    if ($githubState.Responses.Count -eq 0) {
        throw 'Unexpected GitHub request in the test.'
    }
    $githubState.Calls.Add($arguments[5])
    $response = $githubState.Responses.Dequeue()
    $global:LASTEXITCODE = $response.ExitCode
    $response.Body
}
$function:gh = ${function:gh}.GetNewClosure()

$lightweight = New-GitHubResponse @{ ref = $referenceName; object = @{ type = 'commit'; sha = $commit } }
Set-GitHubResponses @($lightweight)
$source = & $sourceScript -Tag $release.Tag -ExpectedCommit $commit.ToUpperInvariant() -Repository 'owner/repo'
Assert-Condition ($source.Tag -ceq $release.Tag -and $source.CommitSha -ceq $commit) 'The lightweight tag was not linked to the build commit.'
Assert-Condition ($script:githubCalls.Count -eq 1 -and $script:githubCalls[0] -ceq $endpoint) 'The exact remote reference was not queried.'

$annotated = New-GitHubResponse @{ ref = $referenceName; object = @{ type = 'tag'; sha = $annotationSha } }
$annotation = New-GitHubResponse @{ sha = $annotationSha; object = @{ type = 'commit'; sha = $commit } }
Set-GitHubResponses @($annotated, $annotation)
$source = & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo'
Assert-Condition ($source.CommitSha -ceq $commit) 'The annotated tag did not resolve to a commit.'
Assert-Condition ($script:githubCalls.Count -eq 2 -and $script:githubCalls[1] -ceq "repos/owner/repo/git/tags/$annotationSha") 'The annotated tag object was not queried by SHA.'

Set-GitHubResponses @(
    $annotated,
    (New-GitHubResponse @{ sha = $annotationSha; object = @{ type = 'tag'; sha = $nestedSha } }),
    (New-GitHubResponse @{ sha = $nestedSha; object = @{ type = 'commit'; sha = $commit } })
)
$source = & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo'
Assert-Condition ($source.CommitSha -ceq $commit -and $script:githubCalls.Count -eq 3) 'Nested annotated tags were not resolved.'

Set-GitHubResponses @((New-GitHubResponse @{ ref = $referenceName; object = @{ type = 'commit'; sha = $otherCommit } }))
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'Tag* does not point to the build commit*'
Set-GitHubResponses @($annotated, (New-GitHubResponse @{ sha = $annotationSha; object = @{ type = 'commit'; sha = $otherCommit } }))
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'Tag* does not point to the build commit*'

foreach ($status in @(1, 4)) {
    Set-GitHubResponses @((New-GitHubResponse @{ message = 'Not Found or authentication required' } -ExitCode $status))
    Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'Could not query*'
}
Set-GitHubResponses @([pscustomobject]@{ Body = '{invalid-json'; ExitCode = 0 })
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'GitHub returned an invalid response*'

foreach ($payload in @(
    @{ ref = 'refs/heads/master'; object = @{ type = 'commit'; sha = $commit } },
    @{ ref = "$referenceName-extra"; object = @{ type = 'commit'; sha = $commit } },
    @{ ref = $referenceName.ToUpperInvariant(); object = @{ type = 'commit'; sha = $commit } },
    @(@{ ref = $referenceName; object = @{ type = 'commit'; sha = $commit } }, @{ ref = $referenceName }),
    @{ unexpected = $true }
)) {
    Set-GitHubResponses @((New-GitHubResponse $payload))
    Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'GitHub did not return the exact release tag reference*'
}

foreach ($target in @(@{ type = 'blob'; sha = $commit }, @{ type = 'commit'; sha = 'invalid' }, @{ type = 'commit'; sha = ('0' * 39) }, $null)) {
    Set-GitHubResponses @((New-GitHubResponse @{ ref = $referenceName; object = $target }))
    Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'Invalid Git object*'
}
Set-GitHubResponses @($annotated, (New-GitHubResponse @{ sha = $otherCommit; object = @{ type = 'commit'; sha = $commit } }))
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'GitHub returned an inconsistent annotated tag.'
Set-GitHubResponses @($annotated, (New-GitHubResponse @{ sha = $annotationSha; object = @{ type = 'tag'; sha = $annotationSha } }))
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'The annotated tag chain*'

$deepChain = @((New-GitHubResponse @{ ref = $referenceName; object = @{ type = 'tag'; sha = (1).ToString('x40') } }))
foreach ($depth in 1..16) {
    $deepChain += New-GitHubResponse @{ sha = $depth.ToString('x40'); object = @{ type = 'tag'; sha = ($depth + 1).ToString('x40') } }
}
Set-GitHubResponses $deepChain
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository 'owner/repo' } 'The annotated tag chain*'
Assert-Condition ($script:githubCalls.Count -eq 17) 'The request depth was not limited.'

Set-GitHubResponses @()
Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $otherCommit -Repository 'owner/repo' } 'The release checkout does not match*'
foreach ($invalidCommit in @('', 'invalid', ('0' * 39), ('g' * 40))) {
    Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $invalidCommit -Repository 'owner/repo' } 'The build commit SHA is invalid.'
}
foreach ($invalidRepository in @('owner', 'owner/repo/extra', '../repo', 'owner/..', 'owner/.', 'owner/repo?query')) {
    Assert-Rejected { & $sourceScript -Tag $release.Tag -ExpectedCommit $commit -Repository $invalidRepository } 'The repository must use the owner/name format*'
}
Assert-Rejected { & $sourceScript -Tag "v$($release.Version)-other" -ExpectedCommit $commit -Repository 'owner/repo' } 'Tag* does not match*'
Assert-Condition ($script:githubCalls.Count -eq 0) 'Invalid inputs must not query the API.'

[pscustomobject]@{ Success = $true; Assertions = $script:checks }
