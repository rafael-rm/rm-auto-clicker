param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$release = & (Join-Path $PSScriptRoot 'Get-Version.ps1')
$project = Join-Path $repositoryRoot 'src/RM.AutoClicker.Desktop/RM.AutoClicker.Desktop.csproj'
$output = Join-Path $repositoryRoot 'artifacts/publish/win-x64'
$packageDirectory = Join-Path $repositoryRoot "artifacts/release/$($release.Version)/win-x64"
$symbolsDirectory = Join-Path $repositoryRoot 'artifacts/symbols'
$logsDirectory = Join-Path $repositoryRoot 'artifacts/logs'
New-Item -ItemType Directory -Path $output,$packageDirectory,$symbolsDirectory,$logsDirectory -Force | Out-Null
$publishLog = Join-Path $logsDirectory 'publish.binlog'
& dotnet publish $project -c $Configuration -p:Platform=x64 -p:RestoreLockedMode=true -o $output "-bl:$publishLog" --nologo
if ($LASTEXITCODE -ne 0) {
    throw 'Publishing failed.'
}

$executable = Join-Path $output $release.AssetName
$productVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
if ($productVersion.Split('+')[0] -cne $release.Version) {
    throw "Executable version '$productVersion' does not match source version '$($release.Version)'."
}

$releaseExecutable = Join-Path $packageDirectory $release.AssetName
Copy-Item -LiteralPath $executable -Destination $releaseExecutable -Force
$hash = (Get-FileHash -LiteralPath $releaseExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $packageDirectory 'SHA256SUMS.txt'), "$hash *$($release.AssetName)`n")

$symbolNames = @('RM.AutoClicker.Core.pdb', 'RM.AutoClicker.Platform.Windows.pdb', [IO.Path]::ChangeExtension($release.AssetName, '.pdb'))
$symbols = @($symbolNames | ForEach-Object { Get-Item -LiteralPath (Join-Path $output $_) -ErrorAction Stop })
$symbolsArchive = Join-Path $symbolsDirectory "RMAutoClicker-$($release.Version)-win-x64-symbols.zip"
Compress-Archive -LiteralPath $symbols.FullName -DestinationPath $symbolsArchive -Force
& (Join-Path $PSScriptRoot 'Test-ReleasePackage.ps1') -Directory $packageDirectory
