param(
    [string]$Executable = '',
    [int]$TimeoutSeconds = 30,
    [switch]$TestIntegration,
    [switch]$TestRestart,
    [ValidateSet('auto', 'pt-BR', 'en-US', 'es')]
    [string]$Language = 'auto'
)

$ErrorActionPreference = 'Stop'
if ($TestRestart -and $TestIntegration) {
    throw 'Restart testing must run without global hotkey integration.'
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Executable)) {
    $release = & (Join-Path $PSScriptRoot 'Get-Version.ps1')
    $Executable = Join-Path $repositoryRoot "artifacts/release/$($release.Version)/win-x64/$($release.AssetName)"
}

$source = (Resolve-Path -LiteralPath $Executable).Path
$testRoot = Join-Path $repositoryRoot ('artifacts/smoke/' + [Guid]::NewGuid().ToString('N'))
$applicationDirectory = Join-Path $testRoot 'aplicativo isolado'
$extractionDirectory = Join-Path $testRoot 'bundle-cache'
New-Item -ItemType Directory -Path $applicationDirectory,$extractionDirectory -Force | Out-Null
$isolatedExecutable = Join-Path $applicationDirectory ([IO.Path]::GetFileName($source))
Copy-Item -LiteralPath $source -Destination $isolatedExecutable

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $isolatedExecutable
$startInfo.ArgumentList.Add('--smoke-test')
if ($TestRestart) {
    $startInfo.ArgumentList.Add('--smoke-restart')
}
if ($TestIntegration) {
    $startInfo.ArgumentList.Add('--smoke-integration')
}
$startInfo.WorkingDirectory = $repositoryRoot
$startInfo.UseShellExecute = $false
$startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = $extractionDirectory
$startInfo.Environment['DOTNET_ROOT'] = Join-Path $testRoot 'no-global-dotnet'
$startInfo.Environment['DOTNET_ROOT_X64'] = Join-Path $testRoot 'no-global-dotnet'
$startInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$startInfo.Environment['RM_AUTO_CLICKER_SMOKE_LANGUAGE'] = $Language
$reports = @()
foreach ($run in 1..2) {
    $reportPath = Join-Path $testRoot "report-$run.json"
    $startInfo.Environment['RM_AUTO_CLICKER_SMOKE_REPORT'] = $reportPath
    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            throw "The startup test exceeded $TimeoutSeconds seconds."
        }
        if ($process.ExitCode -ne 0 -and -not $TestRestart) {
            throw "The application exited with code $($process.ExitCode)."
        }
    } finally {
        $process.Dispose()
    }

    if ($TestRestart) {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $restartedReport = $null
        while ([DateTime]::UtcNow -lt $deadline) {
            if (Test-Path -LiteralPath $reportPath) {
                try { $restartedReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json } catch { }
                if ($null -ne $restartedReport) { break }
            }
            Start-Sleep -Milliseconds 100
        }
        if ($null -eq $restartedReport) {
            throw 'The restarted application did not produce a startup report.'
        }
        $request = Get-Content -LiteralPath ($reportPath + '.restart-request.json') -Raw | ConvertFrom-Json
        if (-not $request.NoticeVerified -or -not $restartedReport.Restarted -or $restartedReport.RestartRequired -or
            $request.ProcessId -eq $restartedReport.ProcessId -or $request.Language -cne $restartedReport.Language) {
            throw 'The application did not restart in the selected language after showing the Preferences notice.'
        }
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        throw 'The application did not produce a startup report.'
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if (-not $report.Success -or $report.ClicksSent -ne 0 -or $report.AutoClickingEnabled -or
        $report.StartButtonEnabled -ne $false -or $report.SmokeSafetyVerified -ne $true -or
        $null -eq $report.InputAttempts -or $report.InputAttempts -ne 0) {
        throw 'The report does not confirm a successful startup without clicks.'
    }
    if (-not $report.ApplicationIconPresent -or -not $report.WindowIconPresent) {
        throw 'The bundled application icon or the window icons are missing.'
    }
    if ($TestIntegration -and -not $report.HotkeysReady) {
        throw "Hotkey integration was not ready: $($report.HotkeyStatus)"
    }
    $expectedLabels = @{ 'pt-BR' = @('Pronto', 'Iniciar'); 'en-US' = @('Ready', 'Start'); 'es' = @('Listo', 'Iniciar') }
    $resourceLanguages = @($report.LanguageResources | ForEach-Object Language | Sort-Object -Unique)
    if (($resourceLanguages -join ',') -cne 'en-US,es,pt-BR') {
        throw 'The executable does not contain all supported language resources.'
    }
    foreach ($resource in $report.LanguageResources) {
        if ($resource.Ready -cne $expectedLabels[$resource.Language][0] -or $resource.Start -cne $expectedLabels[$resource.Language][1]) {
            throw "Incorrect bundled resources for language '$($resource.Language)'."
        }
    }
    if (($Language -ne 'auto' -and $report.Language -cne $Language) -or $report.LocalizedStartLabel -cne $expectedLabels[$report.Language][1]) {
        throw 'The interface does not use the selected display language.'
    }
    if (-not $report.InitialCompositionVerified -or -not $report.InitialActionsVisible -or -not $report.AdvancedRevealVerified -or -not $report.AdvancedOptionsCollapsed -or -not $report.CountdownDisabledByDefault -or -not $report.MetricsBelowStatus -or -not $report.PersistentOperationLayout -or -not $report.HotkeyActionsHiddenByDefault -or -not $report.ResponsiveLayoutVerified -or -not $report.NumericInputEditingVerified -or -not $report.UncommittedNumericInputVerified -or -not $report.NumericPopupDismissalVerified) {
        throw 'The interface does not confirm the expected visual hierarchy and defaults.'
    }

    $modules = @($report.NativeModules)
    if (@($modules | Where-Object { $_.ModuleName -ieq 'Microsoft.UI.Xaml.dll' }).Count -ne 1) {
        throw 'The loaded WinUI runtime could not be identified.'
    }
    $extractionPrefix = [System.IO.Path]::GetFullPath($extractionDirectory) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $report.CoreLibrary.StartsWith($extractionPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $report.CoreLibrary)) {
        throw 'The .NET core library was not loaded from the isolated bundle.'
    }
    foreach ($module in $modules) {
        if (-not $module.FileName.StartsWith($extractionPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Dependency $($module.ModuleName) was not loaded from the isolated bundle: $($module.FileName)"
        }
    }

    $reports += $reportPath
}

[pscustomobject]@{
    Success = $true
    Runtime = $report.Runtime
    Language = $report.Language
    RestartVerified = [bool]$TestRestart
    ExecutableBytes = (Get-Item -LiteralPath $isolatedExecutable).Length
    Reports = $reports
    Runs = $reports.Count
    ClicksSent = $report.ClicksSent
    PrivateRuntimes = $true
    HotkeysReady = $report.HotkeysReady
    InterfaceDefaultsVerified = $true
}
