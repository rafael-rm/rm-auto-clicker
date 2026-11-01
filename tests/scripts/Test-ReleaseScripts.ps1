$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$versionScript = Join-Path $repositoryRoot 'scripts/Get-Version.ps1'
$packageScript = Join-Path $repositoryRoot 'scripts/Test-ReleasePackage.ps1'
$testBase = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts/script-tests'))
$testRoot = Join-Path $testBase ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$versionFile = Join-Path $testRoot 'Directory.Build.props'
$script:checks = 0

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
            throw "Unexpected failure: $($_.Exception.Message)"
        }
        $script:checks++
        return
    }
    throw "The operation should have failed: $ExpectedMessage"
}

try {
    foreach ($version in @('0.0.0', '0.1.0', '1.2.3', '1.2.3-rc.1', '1.2.3-alpha.0', '1.2.3-01a', '1.2.3-0')) {
        Set-Content -LiteralPath $versionFile -Value "<Project><PropertyGroup><Version>$version</Version></PropertyGroup></Project>"
        $release = & $versionScript -VersionFile $versionFile -Tag "v$version"
        Assert-Condition ($release.Version -ceq $version) 'The version was changed.'
        Assert-Condition ($release.IsPrerelease -eq $version.Contains('-')) 'The prerelease classification is incorrect.'
        Assert-Condition ($release.AssetName -ceq "RMAutoClicker-$version-win-x64.exe") 'The artifact name is incorrect.'
    }

    foreach ($version in @('', '1.0', '01.2.3', '1.02.3', '1.2.03', 'v1.2.3', '1.2.3-01', '1.2.3-alpha.01', '1.2.3-', '1.2.3+build', '../bad')) {
        Set-Content -LiteralPath $versionFile -Value "<Project><PropertyGroup><Version>$version</Version></PropertyGroup></Project>"
        Assert-Rejected { & $versionScript -VersionFile $versionFile } 'Invalid version:*'
    }

    Set-Content -LiteralPath $versionFile -Value '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>'
    Assert-Rejected { & $versionScript -VersionFile $versionFile -Tag 'v1.2.4' } 'Tag*'
    Assert-Rejected { & $versionScript -VersionFile $versionFile -Tag 'V1.2.3' } 'Tag*'
    foreach ($content in @('<Project />', '<Project><PropertyGroup><Version>1.0.0</Version><Version>2.0.0</Version></PropertyGroup></Project>')) {
        Set-Content -LiteralPath $versionFile -Value $content
        Assert-Rejected { & $versionScript -VersionFile $versionFile } 'Define exactly one version*'
    }

    $release = & $versionScript
    $packageDirectory = Join-Path $testRoot 'package'
    New-Item -ItemType Directory -Path $packageDirectory | Out-Null
    $executable = Join-Path $packageDirectory $release.AssetName
    $manifest = Join-Path $packageDirectory 'SHA256SUMS.txt'
    [IO.File]::WriteAllBytes($executable, [byte[]]@(1, 2, 3))
    $hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    $validManifest = "$hash *$($release.AssetName)`n"
    [IO.File]::WriteAllText($manifest, $validManifest)
    $package = & $packageScript -Directory $packageDirectory -Tag $release.Tag
    Assert-Condition ($package.SHA256 -ceq $hash) 'The verified hash is incorrect.'
    $package = & $packageScript -Directory $packageDirectory -ExpectedSHA256 $hash.ToUpperInvariant()
    Assert-Condition ($package.SHA256 -ceq $hash) 'The expected hash from CI was changed.'
    foreach ($invalidHash in @('', 'invalid', ('0' * 63), ('g' * 64))) {
        Assert-Rejected { & $packageScript -Directory $packageDirectory -ExpectedSHA256 $invalidHash } 'The expected SHA-256 hash from CI is invalid.'
    }
    Assert-Rejected { & $packageScript -Directory $packageDirectory -ExpectedSHA256 ('0' * 64) } 'The executable does not match the SHA-256 hash recorded by CI.'

    $unexpected = Join-Path $packageDirectory 'debug.pdb'
    Set-Content -LiteralPath $unexpected -Value 'symbols'
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The package must contain only*'
    Remove-Item -LiteralPath $unexpected

    $unexpectedDirectory = Join-Path $packageDirectory 'extra'
    New-Item -ItemType Directory -Path $unexpectedDirectory | Out-Null
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The package must contain only*'
    Remove-Item -LiteralPath $unexpectedDirectory

    [IO.File]::WriteAllText($manifest, "$validManifest$validManifest")
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The checksum does not match*'
    [IO.File]::WriteAllText($manifest, "$hash *other.exe`n")
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The checksum does not match*'
    [IO.File]::WriteAllText($manifest, $validManifest)
    [IO.File]::WriteAllBytes($executable, [byte[]]@(4, 5, 6))
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The checksum does not match*'
    $modifiedHash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($manifest, "$modifiedHash *$($release.AssetName)`n")
    $modifiedPackage = & $packageScript -Directory $packageDirectory
    Assert-Condition ($modifiedPackage.SHA256 -ceq $modifiedHash) 'The modified package should be consistent with its own manifest.'
    Assert-Rejected { & $packageScript -Directory $packageDirectory -ExpectedSHA256 $hash } 'The executable does not match the SHA-256 hash recorded by CI.'
    [IO.File]::WriteAllBytes($executable, [byte[]]@())
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The executable is empty.'
    Remove-Item -LiteralPath $executable
    Assert-Rejected { & $packageScript -Directory $packageDirectory } 'The package must contain only*'

    $ruleset = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/rulesets/protect-release-tags.json') -Raw | ConvertFrom-Json
    Assert-Condition ($ruleset.target -ceq 'tag' -and $ruleset.enforcement -ceq 'active') 'The ruleset must protect tags and be active.'
    Assert-Condition (@($ruleset.conditions.ref_name.include).Count -eq 1 -and $ruleset.conditions.ref_name.include[0] -ceq 'refs/tags/v*') 'The ruleset must cover exactly the v* tags.'
    Assert-Condition (@($ruleset.conditions.ref_name.exclude).Count -eq 0 -and @($ruleset.bypass_actors).Count -eq 0) 'The ruleset must not contain exclusions or bypass actors.'
    Assert-Condition (@($ruleset.rules).Count -eq 2 -and $ruleset.rules.type -contains 'update' -and $ruleset.rules.type -contains 'deletion') 'The ruleset must block updates and deletions without blocking creation.'
    Assert-Condition (($ruleset.rules | Where-Object type -eq 'update').parameters.update_allows_fetch_and_merge -eq $false) 'The update restriction must not allow updates from upstream.'

    $sourceTests = & (Join-Path $PSScriptRoot 'Test-ReleaseSource.ps1')
    Assert-Condition $sourceTests.Success 'Release source verification failed.'
    $script:checks += $sourceTests.Assertions
    $publicationTests = & (Join-Path $PSScriptRoot 'Test-PublishRelease.ps1')
    Assert-Condition $publicationTests.Success 'Release publication verification failed.'
    $script:checks += $publicationTests.Assertions
    [pscustomobject]@{ Success = $true; Assertions = $script:checks }
} finally {
    $resolved = (Resolve-Path -LiteralPath $testRoot).ProviderPath
    if (-not $resolved.StartsWith($testBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The temporary directory is outside the test area.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
