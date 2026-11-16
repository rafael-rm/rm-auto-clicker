<img src="assets/branding/rm-auto-clicker-logo.png" alt="RM Auto Clicker logo" width="80">

# RM Auto Clicker

Automate clicks on Windows with adjustable speed, customizable hotkeys, and a simple interface.

## How to use

1. Download `RMAutoClicker-<version>-win-x64.exe` from the repository's **Releases** section.
2. Open the app and choose the speed and mouse button.
3. Move the cursor to where you want to click and press **F8** to start.
4. Press **F8** again to stop.

You can also use the **Start** and **Stop** buttons. The emergency stop shortcut is **Ctrl+Shift+F10**.

The app is distributed as a single EXE for **64-bit (x64) Windows 10 or 11**. No installation is required, and .NET does not need to be installed separately.

## Features

- Speed from **1 to 100 clicks per second**.
- Left, right, or middle mouse button.
- Customizable start/stop shortcut with Ctrl, Alt, and Shift combinations.
- Advanced options for initial delay, click limit, time limit, and interval variation.
- Click count and session time counters.
- Light, dark, and system themes.
- Available in Portuguese, English, and Spanish.

The initial delay is off by default. Clicking is paused while the cursor is over the app's own window and resumes when the cursor leaves the window.

## Preferences and system tray

Under **Preferences**, choose a theme and language. After changing the language, select **Restart now** to apply it. **Minimize to the system tray** is off by default, and the setting is saved when you select it.

To change the shortcut, select **Change shortcut**, press the desired key combination, and confirm with **Apply shortcut**. If the combination is unavailable, the previous shortcut remains active.

By default, minimizing keeps the window on the taskbar. Enable **Minimize to the system tray** to hide the window from the taskbar and place its icon in the notification area. To reopen the app, double-click the icon near the clock or select **Open** from its menu. Closing the window exits the app and stops clicking.

## Settings and logs

Files are stored in:

```text
%LOCALAPPDATA%\RM Corporation\RM Auto Clicker\
```

Preferences are saved in `settings.json`, and logs are stored in `logs`.

## Technical documentation

For instructions on building, testing, or contributing to the project, see the [technical guide](docs/technical.md) (in Portuguese).
