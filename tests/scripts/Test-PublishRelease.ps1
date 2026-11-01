$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testBase = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/script-tests'))
$testRoot = Join-Path $testBase ([Guid]::NewGuid().ToString('N'))
$scriptDirectory = Join-Path $testRoot 'scripts'
$packageDirectory = Join-Path $testRoot 'package'
New-Item -ItemType Directory -Path $scriptDirectory,$packageDirectory -Force | Out-Null
foreach ($name in @('Publish-Release.ps1', 'Get-Version.ps1', 'Test-ReleasePackage.ps1', 'Test-ReleaseSource.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "scripts/$name") -Destination $scriptDirectory
}
$publishScript = Join-Path $scriptDirectory 'Publish-Release.ps1'
$commit = 'a' * 40
$state = [pscustomobject]@{
    Tag = ''; Version = ''; Hash = ''; Commit = $commit; Root = $testRoot
    Responses = $null; Calls = $null; GitCalls = 0
}
$script:checks = 0

function Assert-Condition {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try { & $Action | Out-Null } catch {
        if ($_.Exception.Message -notlike $ExpectedMessage) {
            throw "Unexpected failure (expected: $ExpectedMessage): $($_.Exception.Message)"
        }
        $script:checks++
        return
    }
    throw "The operation should have failed: $ExpectedMessage"
}

function New-Response {
    param([string]$Kind, [object]$Payload = @{}, [int]$ExitCode = 0)
    [pscustomobject]@{ Kind = $Kind; Body = ($Payload | ConvertTo-Json -Depth 8 -Compress); ExitCode = $ExitCode }
}

function Initialize-Case {
    param([string]$Version = '1.2.3')
    $state.Version = $Version
    $state.Tag = "v$Version"
    $state.Calls = [Collections.Generic.List[object]]::new()
    $state.GitCalls = 0
    $state.Responses = [Collections.Generic.Queue[object]]::new()
    Set-Content -LiteralPath (Join-Path $testRoot 'Directory.Build.props') -Value "<Project><PropertyGroup><Version>$Version</Version></PropertyGroup></Project>"
    Get-ChildItem -LiteralPath $packageDirectory -File | Remove-Item -Force
    $asset = "RMAutoClicker-$Version-win-x64.exe"
    $executable = Join-Path $packageDirectory $asset
    [IO.File]::WriteAllBytes($executable, [byte[]]@(1, 2, 3))
    $state.Hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $packageDirectory 'SHA256SUMS.txt'), "$($state.Hash) *$asset`n")
}

function Set-Responses {
    param([object[]]$Responses)
    foreach ($response in $Responses) { $state.Responses.Enqueue($response) }
}

function New-SourceResponse {
    param([string]$TargetCommit = $commit, [int]$ExitCode = 0)
    New-Response Source @{ ref = "refs/tags/$($state.Tag)"; object = @{ type = 'commit'; sha = $TargetCommit } } $ExitCode
}

function New-DraftResponse {
    param([hashtable]$Overrides = @{}, [int]$ExitCode = 0)
    $draft = @{ databaseId = 123; tagName = $state.Tag; isDraft = $true; isPrerelease = $state.Version.Contains('-') }
    foreach ($key in $Overrides.Keys) { $draft[$key] = $Overrides[$key] }
    New-Response Draft $draft $ExitCode
}

function Invoke-Publication {
    param([string]$ExpectedHash = $state.Hash)
    & $publishScript -Tag $state.Tag -Directory $packageDirectory -ExpectedCommit $state.Commit -ExpectedSHA256 $ExpectedHash -Repository 'owner/repo'
}

function git {
    $arguments = @($args)
    if ($arguments.Count -ne 5 -or $arguments[0] -cne '-C' -or $arguments[1] -cne $state.Root -or
        ($arguments[2..4] -join ' ') -cne 'rev-parse --verify HEAD^{commit}') {
        throw 'Unexpected Git command in the offline publication test.'
    }
    $state.GitCalls++
    $global:LASTEXITCODE = 0
    $state.Commit
}
$function:git = ${function:git}.GetNewClosure()

function gh {
    $arguments = @($args)
    if ($arguments.Count -ge 3 -and ($arguments[0..1] -join ' ') -ceq 'release create') {
        $kind = 'Create'
    } elseif (($arguments -join ' ') -ceq "release view $($state.Tag) --repo owner/repo --json databaseId,tagName,isDraft,isPrerelease") {
        $kind = 'Draft'
    } elseif ($arguments.Count -ge 6 -and ($arguments[0..3] -join ' ') -ceq 'api --hostname github.com --method') {
        if ($arguments[4] -ceq 'GET' -and $arguments[5] -ceq "repos/owner/repo/git/ref/tags/$($state.Tag)") { $kind = 'Source' }
        elseif ($arguments[4] -ceq 'PATCH' -and $arguments[5] -ceq 'repos/owner/repo/releases/123') { $kind = 'Publish' }
        else { throw 'Unexpected GitHub endpoint in the offline publication test.' }
    } else { throw 'Unexpected GitHub command in the offline publication test.' }
    if ($state.Responses.Count -eq 0) { throw 'No response configured; external GitHub access is forbidden in this test.' }
    $response = $state.Responses.Dequeue()
    if ($response.Kind -cne $kind) { throw "Expected $($response.Kind) before $kind." }
    $state.Calls.Add([pscustomobject]@{ Kind = $kind; Arguments = $arguments })
    $global:LASTEXITCODE = $response.ExitCode
    $response.Body
}
$function:gh = ${function:gh}.GetNewClosure()

try {
    # A newer stable version followed by an older one must never force latest=true.
    foreach ($version in @('1.2.3', '1.1.0', '1.3.0-rc.1')) {
        Initialize-Case $version
        Set-Responses @((New-SourceResponse), (New-Response Create), (New-DraftResponse), (New-SourceResponse), (New-Response Publish))
        $result = Invoke-Publication
        $policy = if ($version.Contains('-')) { 'false' } else { 'legacy' }
        Assert-Condition ($result.LatestPolicy -ceq $policy) 'The publication selected the wrong latest policy.'
        Assert-Condition (($state.Calls.Kind -join ',') -ceq 'Source,Create,Draft,Source,Publish') 'The publication order changed.'
        Assert-Condition ($state.GitCalls -eq 2) 'The checkout must be checked before creation and publication.'
        $creation = $state.Calls[1].Arguments
        foreach ($flag in @('--draft', '--verify-tag', '--generate-notes', '--latest=false')) {
            Assert-Condition ($creation -ccontains $flag) "The draft must use $flag."
        }
        Assert-Condition ($creation[2] -ceq $state.Tag -and $creation -ccontains 'owner/repo') 'The draft must use the expected tag and repository.'
        Assert-Condition (($creation -ccontains '--prerelease') -eq $version.Contains('-')) 'The prerelease classification changed.'
        Assert-Condition ($creation -cnotcontains '--latest' -and $creation -cnotcontains '--clobber') 'Draft creation must not force latest or overwrite assets.'
        $publication = $state.Calls[4].Arguments
        Assert-Condition (($publication[6..9] -join ' ') -ceq "--field draft=false --raw-field make_latest=$policy") 'The API must publish the validated draft with the explicit latest policy.'
        Assert-Condition ($state.Responses.Count -eq 0) 'An expected publication step was skipped.'
    }

    Initialize-Case
    Assert-Rejected { Invoke-Publication -ExpectedHash ('0' * 64) } 'The executable does not match the SHA-256 hash recorded by CI.'
    Assert-Condition ($state.Calls.Count -eq 0) 'A hash mismatch must stop before accessing GitHub.'

    Initialize-Case
    Set-Responses @((New-SourceResponse -TargetCommit ('b' * 40)))
    Assert-Rejected { Invoke-Publication } 'Tag*does not point to the build commit*'
    Assert-Condition ($state.Calls.Count -eq 1) 'A moved tag must not create a draft.'

    Initialize-Case
    Set-Responses @((New-SourceResponse -ExitCode 1))
    Assert-Rejected { Invoke-Publication } 'Could not query the release reference on GitHub.'
    Assert-Condition ($state.Calls.Count -eq 1) 'A failed source query must not create a draft.'

    Initialize-Case
    Set-Responses @((New-SourceResponse), (New-Response Create -ExitCode 1))
    Assert-Rejected { Invoke-Publication } 'Could not prepare the draft release. Existing releases are not overwritten.'
    Assert-Condition ($state.Calls.Count -eq 2) 'An existing release or failed upload must not proceed to publication.'

    Initialize-Case
    Set-Responses @((New-SourceResponse), (New-Response Create), (New-DraftResponse -ExitCode 1))
    Assert-Rejected { Invoke-Publication } 'Could not identify the draft release*'
    Assert-Condition ($state.Calls.Count -eq 3) 'A failed draft query must not proceed to publication.'

    foreach ($override in @(@{ databaseId = 0 }, @{ databaseId = 'invalid' }, @{ tagName = 'v9.9.9' }, @{ isDraft = $false }, @{ isPrerelease = $true })) {
        Initialize-Case
        Set-Responses @((New-SourceResponse), (New-Response Create), (New-DraftResponse -Overrides $override))
        Assert-Rejected { Invoke-Publication } 'GitHub did not return the expected draft release.'
        Assert-Condition ($state.Calls.Count -eq 3) 'An unexpected release must never be published.'
    }

    Initialize-Case
    $invalid = New-DraftResponse
    $invalid.Body = 'invalid json'
    Set-Responses @((New-SourceResponse), (New-Response Create), $invalid)
    Assert-Rejected { Invoke-Publication } 'GitHub returned an invalid response for the draft release.'

    foreach ($queryFails in @($false, $true)) {
        Initialize-Case
        $secondSource = if ($queryFails) { New-SourceResponse -ExitCode 1 } else { New-SourceResponse -TargetCommit ('b' * 40) }
        Set-Responses @((New-SourceResponse), (New-Response Create), (New-DraftResponse), $secondSource)
        $message = if ($queryFails) { 'Could not query the release reference on GitHub.' } else { 'Tag*does not point to the build commit*' }
        Assert-Rejected { Invoke-Publication } $message
        Assert-Condition ($state.Calls.Count -eq 4) 'A failed second source check must leave the draft unpublished.'
    }

    Initialize-Case
    Set-Responses @((New-SourceResponse), (New-Response Create), (New-DraftResponse), (New-SourceResponse), (New-Response Publish -ExitCode 1))
    Assert-Rejected { Invoke-Publication } 'Could not publish the validated draft*'
    Assert-Condition ($state.Calls.Count -eq 5) 'Publication failure must not trigger an automatic overwrite or retry.'

    $global:LASTEXITCODE = 0
    [pscustomobject]@{ Success = $true; Assertions = $script:checks }
} finally {
    $resolved = (Resolve-Path -LiteralPath $testRoot).ProviderPath
    if (-not $resolved.StartsWith($testBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The temporary directory is outside the test area.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
