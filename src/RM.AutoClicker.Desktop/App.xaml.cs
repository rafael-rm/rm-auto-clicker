using System.Globalization;
using Microsoft.UI.Xaml;
using RM.AutoClicker.Core.Abstractions;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Desktop.Localization;
using RM.AutoClicker.Platform.Windows.Persistence;
using RM.AutoClicker.Platform.Windows.Lifecycle;

namespace RM.AutoClicker.Desktop;

public partial class App : Application
{
    private readonly ISettingsStore _settings;
    private readonly SettingsLoadResult _loadedSettings;
    private readonly IAppStrings _strings;
    private Window? _window;
    private SingleInstanceGuard? _instance;

    public App()
    {
        try
        {
            var smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal);
            var restartTest = smokeTest && Environment.GetCommandLineArgs().Contains("--smoke-restart", StringComparer.Ordinal);
            var reportPath = Environment.GetEnvironmentVariable("RM_AUTO_CLICKER_SMOKE_REPORT");
            if (restartTest && (string.IsNullOrWhiteSpace(reportPath) || !Path.IsPathFullyQualified(reportPath)))
                throw new InvalidOperationException("Restart testing requires an absolute RM_AUTO_CLICKER_SMOKE_REPORT path.");
            _settings = restartTest ? new SettingsStore(reportPath + ".preferences") : new SettingsStore();
            _loadedSettings = smokeTest && !restartTest ? new(new(), CanSave: false) : _settings.Load();
            var preference = smokeTest && !restartTest ? Environment.GetEnvironmentVariable("RM_AUTO_CLICKER_SMOKE_LANGUAGE") ?? "auto" : _loadedSettings.Settings.Language;
            var language = AppStrings.ResolveLanguage(preference, CultureInfo.CurrentUICulture.Name);
            _strings = MrtAppStrings.Create(language);
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            InitializeComponent();
            UnhandledException += (_, args) => AppDiagnostics.Write("UnhandledException", args.Exception);
        }
        catch (Exception exception)
        {
            AppDiagnostics.Write("StartupFailed", exception);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var smokeTest = Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.Ordinal);
        if (!smokeTest)
        {
            _instance = new SingleInstanceGuard();
            if (!_instance.TryAcquire())
            {
                _instance.Dispose();
                _instance = null;
                Exit();
                return;
            }
        }

        var testIntegration = smokeTest && Environment.GetCommandLineArgs().Contains("--smoke-integration", StringComparer.Ordinal);
        _window = new MainWindow(_strings, _settings, _loadedSettings, smokeTest, testIntegration);
        _window.Closed += (_, _) => _instance?.Dispose();
        _window.Activate();
    }
}
