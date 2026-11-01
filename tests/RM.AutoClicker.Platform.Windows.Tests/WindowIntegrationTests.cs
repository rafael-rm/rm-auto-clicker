using RM.AutoClicker.Tests;
using System.ComponentModel;
using RM.AutoClicker.Core.Models;
using RM.AutoClicker.Platform.Windows.Hotkeys;

namespace RM.AutoClicker.Platform.Windows.Tests;

public sealed class WindowIntegrationTests
{
    [Theory]
    [InlineData(0x0312, 100)]
    [InlineData(0x0218, 4)]
    public void EmergencyAndSuspendRequestOnlyOneStop(uint message, int parameter)
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var stops = 0;
        var toggles = 0;
        integration.StopRequested += () => stops++;
        integration.ToggleRequested += () => toggles++;

        api.Send(message, (nuint)parameter);

        Assert.Equal(1, stops);
        Assert.Equal(0, toggles);
    }

    [Fact]
    public void ObsoleteAndUnknownHotkeyIdsCannotActivateTheSession()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var originalId = api.Hotkeys.Last();
        Assert.True(integration.TryChangeHotkey(new(0x41, HotkeyModifiers.Control), out _));
        var currentId = api.Hotkeys.Last();
        Assert.NotEqual(originalId, currentId);
        var toggles = 0;
        var stops = 0;
        integration.ToggleRequested += () => toggles++;
        integration.StopRequested += () => stops++;

        api.Send(0x0312, (nuint)originalId);
        api.Send(0x0312, 999);
        Assert.Equal(0, toggles);
        Assert.Equal(0, stops);

        api.Send(0x0312, (nuint)currentId);
        Assert.Equal(1, toggles);
        Assert.Equal(0, stops);
    }

    [Fact]
    public void FailedEmergencyRegistrationDoesNotAcceptItsHotkeyMessage()
    {
        var api = new WindowApi { CanRegisterEmergency = false };
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var stops = 0;
        var toggles = 0;
        integration.StopRequested += () => stops++;
        integration.ToggleRequested += () => toggles++;

        api.Send(0x0312, 100);

        Assert.False(integration.EmergencyReady);
        Assert.Equal(0, stops);
        Assert.Equal(0, toggles);
    }

    [Fact]
    public void NativeMessagesAfterDisposalDoNotRequestAnyAction()
    {
        var api = new WindowApi();
        var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var toggleId = api.Hotkeys.Last();
        var actions = 0;
        integration.ToggleRequested += () => actions++;
        integration.StopRequested += () => actions++;
        integration.ShowRequested += () => actions++;
        integration.ExitRequested += () => actions++;
        integration.Dispose();

        api.Send(0x0312, (nuint)toggleId);
        api.Send(0x0312, 100);
        api.Send(0x0218, 4);
        api.Send(0x02B1, 7);
        api.Send(0x8001, detail: 0x0203);
        api.Send(WindowApi.TaskbarMessage);

        Assert.Equal(0, actions);
        Assert.Equal(1, api.TrayAdds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HotkeyStatusOmitsRedundantEmergencyInstructionsButPreservesFailures(bool emergencyAvailable)
    {
        var api = new WindowApi { CanRegisterEmergency = emergencyAvailable };
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        Assert.Equal(emergencyAvailable, integration.EmergencyReady);
        Assert.Equal(emergencyAvailable, integration.HotkeysReady);
        if (emergencyAvailable)
        {
            Assert.Contains("disponível", integration.Status);
            Assert.DoesNotContain("Ctrl+Shift+F10", integration.Status);
        }
        else
        {
            Assert.Contains("Ctrl+Shift+F10 está indisponível", integration.Status);
            Assert.Contains("reabra", integration.Status);
        }
    }

    [Fact]
    public void FailedSessionRegistrationBlocksStartingAndPreservesTheNativeError()
    {
        var api = new WindowApi { SessionAvailable = false, LastError = 1702 };
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);

        Assert.False(integration.HotkeysReady);
        Assert.Contains("1702", integration.Status);
        Assert.True(integration.TryChangeHotkey(new(0x78), out _));
        Assert.False(integration.HotkeysReady);
        Assert.Contains("bloqueio/desconexão", integration.Status);
        integration.Dispose();
        Assert.Equal(0, api.SessionUnregisters);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void SessionChangesRequestStopWithoutRequestingRestart(int change)
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var stops = 0;
        var toggles = 0;
        integration.StopRequested += () => stops++;
        integration.ToggleRequested += () => toggles++;

        api.Send(0x02B1, (nuint)change);
        api.Send(0x02B1, 8);

        Assert.Equal(1, stops);
        Assert.Equal(0, toggles);
    }

    [Fact]
    public void FailedTrayCreationKeepsTheWindowVisibleAndAllowsLaterRecovery()
    {
        var api = new WindowApi { TrayAvailable = false };
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);

        Assert.True(integration.HotkeysReady);
        Assert.False(integration.Hide());
        Assert.Equal([true], api.Visibility);
        Assert.Contains("Bandeja indisponível", integration.Status);

        api.TrayAvailable = true;
        Assert.True(integration.Hide());
        Assert.Equal([true, false], api.Visibility);
        Assert.DoesNotContain("indisponível", integration.Status);
        integration.Show();
        Assert.True(api.Visibility[^1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplorerRecreationChecksWhetherTheIconWasRestored(bool restored)
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        Assert.True(integration.Hide());
        api.TrayAvailable = restored;

        api.Send(WindowApi.TaskbarMessage);

        Assert.Equal(2, api.TrayAdds);
        Assert.Equal(restored ? new[] { false } : [false, true], api.Visibility);
        Assert.Equal(!restored, integration.Status.Contains("Bandeja indisponível"));
    }

    [Fact]
    public void FailedTrayUpdateRestoresAHiddenWindow()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        integration.Hide();
        api.TrayAvailable = false;

        Assert.True(integration.TryChangeHotkey(new(0x78), out _));

        Assert.Equal([false, true], api.Visibility);
        Assert.Contains("Bandeja indisponível", integration.Status);
    }

    [Fact]
    public void DisposeReleasesOnlyAcquiredResourcesAndIsIdempotent()
    {
        var api = new WindowApi();
        var integration = new WindowIntegration(1, api, TestStrings.Portuguese);

        integration.Dispose();
        integration.Dispose();

        Assert.Equal(2, api.HotkeyUnregisters.Count);
        Assert.Equal(1, api.SessionUnregisters);
        Assert.Equal(1, api.TrayDeletes);
        Assert.Equal(1, api.Detaches);
        Assert.False(integration.HotkeysReady);
        Assert.Throws<ObjectDisposedException>(() => integration.Hide());
    }

    [Fact]
    public void ConstructorFailureReleasesRegistrationsAlreadyAcquired()
    {
        var api = new WindowApi { ThrowOnTrayAdd = true };

        Assert.Throws<InvalidOperationException>(() => new WindowIntegration(1, api, TestStrings.Portuguese));

        Assert.Equal(2, api.HotkeyUnregisters.Count);
        Assert.Equal(1, api.SessionUnregisters);
        Assert.Equal(1, api.Detaches);
    }

    [Fact]
    public void SubclassFailureReportsTheNativeErrorWithoutRegisteringOtherResources()
    {
        var api = new WindowApi { CanAttach = false, LastError = 5 };

        var failure = Assert.Throws<Win32Exception>(() => new WindowIntegration(1, api, TestStrings.Portuguese));

        Assert.Equal(5, failure.NativeErrorCode);
        Assert.Empty(api.Hotkeys);
        Assert.Equal(0, api.Detaches);
    }

    [Fact]
    public void CallbackFailureRequestsStopAndDisablesFurtherStarts()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var stops = 0;
        integration.ToggleRequested += () => throw new InvalidOperationException("Falha simulada.");
        integration.StopRequested += () => stops++;

        api.Send(0x0312, 2);

        Assert.Equal(1, stops);
        Assert.False(integration.HotkeysReady);
        Assert.Contains("Falha na integração", integration.Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TrayMenuForwardsTheSelectedAction(int selected)
    {
        var api = new WindowApi { MenuSelection = (uint)selected };
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        var actions = new List<int>();
        integration.ShowRequested += () => actions.Add(1);
        integration.StopRequested += () => actions.Add(2);
        integration.ExitRequested += () => actions.Add(3);

        api.Send(0x8001, detail: 0x0205);

        Assert.Equal([selected], actions);
    }

    [Fact]
    public void HotkeyConflictKeepsThePreviousRegistration()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        api.CanRegisterToggle = false;

        Assert.False(integration.TryChangeHotkey(new(0x78), out var error));

        Assert.NotNull(error);
        Assert.Empty(api.HotkeyUnregisters);
        Assert.True(integration.HotkeysReady);
    }

    [Fact]
    public void RegistersCustomKeyAndModifiersAndKeepsEmergencyFixed()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese, new(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt));

        Assert.Equal(0x79u, api.Registrations[0].Key);
        Assert.Equal(0x4006u, api.Registrations[0].Modifiers);
        Assert.Equal(0x41u, api.Registrations[1].Key);
        Assert.Equal(0x4003u, api.Registrations[1].Modifiers);
        Assert.Contains("Ctrl+Alt+A", integration.Status);
        Assert.True(integration.HotkeysReady);
    }

    [Fact]
    public void DifferentModifiersOnTheSameKeyRequireANewRegistration()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese, new(0x41, HotkeyModifiers.Control));

        Assert.True(integration.TryChangeHotkey(new(0x41, HotkeyModifiers.Control | HotkeyModifiers.Shift), out _));

        Assert.Equal(3, api.Registrations.Count);
        Assert.Equal(0x4006u, api.Registrations[^1].Modifiers);
        Assert.Equal([2], api.HotkeyUnregisters);
    }

    [Fact]
    public void RegisteringTheSameGestureDoesNotConflictWithItself()
    {
        var api = new WindowApi();
        var hotkey = new HotkeyGesture(0x41, HotkeyModifiers.Control | HotkeyModifiers.Alt);
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese, hotkey);

        Assert.True(integration.TryChangeHotkey(hotkey, out _));

        Assert.Equal(2, api.Registrations.Count);
        Assert.Empty(api.HotkeyUnregisters);
    }

    [Fact]
    public void ReservedCandidateDoesNotChangeNativeRegistrations()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);

        Assert.Throws<AppValidationException>(() => integration.TryChangeHotkey(HotkeyGesture.Emergency, out _));

        Assert.Equal(2, api.Registrations.Count);
        Assert.Empty(api.HotkeyUnregisters);
        Assert.True(integration.HotkeysReady);
    }

    [Fact]
    public void FailedOldRegistrationRemovalRollsBackTheNewGesture()
    {
        var api = new WindowApi { FailUnregisterId = 2 };
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);

        Assert.False(integration.TryChangeHotkey(new(0x41, HotkeyModifiers.Control), out var error));

        Assert.Contains("preservada", error);
        Assert.Equal([2, 1], api.HotkeyUnregisters);
        Assert.True(integration.HotkeysReady);
    }

    [Fact]
    public void TrayStatusOnlyUpdatesWhenTheSessionStateChanges()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        integration.UpdateSessionStatus("Executando");
        integration.UpdateSessionStatus("Executando");
        Assert.Single(api.TrayTips);
        Assert.Contains("Executando", api.TrayTips[0]);
        Assert.Contains("F8", api.TrayTips[0]);
        Assert.Contains("Ctrl+Shift+F10", api.TrayTips[0]);
    }

    [Fact]
    public void TrayUpdateFailureRestoresAHiddenWindow()
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Portuguese);
        Assert.True(integration.Hide());
        api.TrayAvailable = false;
        integration.UpdateSessionStatus("Executando");
        Assert.Equal([false, true], api.Visibility);
    }

    private sealed class WindowApi : IWindowIntegrationApi
    {
        public const uint TaskbarMessage = 0xC001;
        public int LastError { get; init; }
        public bool CanAttach { get; init; } = true;
        public bool CanRegisterToggle { get; set; } = true;
        public bool CanRegisterEmergency { get; init; } = true;
        public bool SessionAvailable { get; init; } = true;
        public bool TrayAvailable { get; set; } = true;
        public bool ThrowOnTrayAdd { get; init; }
        public int? FailUnregisterId { get; init; }
        public int SessionUnregisters { get; private set; }
        public int TrayAdds { get; private set; }
        public int TrayDeletes { get; private set; }
        public int Detaches { get; private set; }
        public uint MenuSelection { get; init; }
        public List<int> Hotkeys { get; } = [];
        public List<(uint Modifiers, uint Key)> Registrations { get; } = [];
        public List<int> HotkeyUnregisters { get; } = [];
        public List<bool> Visibility { get; } = [];
        public List<string> TrayTips { get; } = [];
        public string[] MenuLabels { get; private set; } = [];
        private WindowSubclassProcedure? _procedure;

        public void Send(uint message, nuint parameter = 0, nint detail = 0) => _procedure!(1, message, parameter, detail, 1, 0);
        public bool Attach(nint window, WindowSubclassProcedure procedure) { _procedure = procedure; return CanAttach; }
        public void Detach(nint window, WindowSubclassProcedure procedure) => Detaches++;
        public nint ForwardMessage(nint window, uint message, nuint parameter, nint detail) => 0;
        public bool RegisterHotkey(nint window, int id, uint modifiers, uint key)
        {
            if (id == 100 ? !CanRegisterEmergency : !CanRegisterToggle)
                return false;
            Hotkeys.Add(id);
            Registrations.Add((modifiers, key));
            return true;
        }
        public bool UnregisterHotkey(nint window, int id) { HotkeyUnregisters.Add(id); return id != FailUnregisterId; }
        public uint RegisterTaskbarMessage() => TaskbarMessage;
        public bool RegisterSessionNotifications(nint window) => SessionAvailable;
        public void UnregisterSessionNotifications(nint window) => SessionUnregisters++;
        public bool AddTrayIcon(nint window, string tip)
        {
            TrayAdds++;
            if (ThrowOnTrayAdd)
                throw new InvalidOperationException("Falha simulada.");
            return TrayAvailable;
        }
        public bool UpdateTrayIcon(string tip) { TrayTips.Add(tip); return TrayAvailable; }
        public void DeleteTrayIcon() => TrayDeletes++;
        public void SetVisible(nint window, bool visible) => Visibility.Add(visible);
        public uint ShowTrayMenu(nint window, string openLabel, string stopLabel, string exitLabel)
        {
            MenuLabels = [openLabel, stopLabel, exitLabel];
            return MenuSelection;
        }
    }

    [Theory]
    [InlineData("pt-BR", "disponível", "Parar", "Sair")]
    [InlineData("en-US", "available", "Stop", "Exit")]
    [InlineData("es", "disponible", "Detener", "Salir")]
    public void LocalizesTrayMenuAndHotkeyFeedback(string language, string available, string stop, string exit)
    {
        var api = new WindowApi();
        using var integration = new WindowIntegration(1, api, TestStrings.Create(language));
        Assert.Contains(available, integration.Status);
        api.Send(0x8001, detail: 0x0205);
        Assert.Equal(stop, api.MenuLabels[1]);
        Assert.Equal(exit, api.MenuLabels[2]);
        api.CanRegisterToggle = false;
        Assert.False(integration.TryChangeHotkey(new(0x78), out var error));
        Assert.Equal(TestStrings.Create(language).Get("HotkeyConflict", "F9", 0), error);
    }
}
