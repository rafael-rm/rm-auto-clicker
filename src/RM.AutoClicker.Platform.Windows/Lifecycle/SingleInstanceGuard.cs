using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RM.AutoClicker.Platform.Windows.Lifecycle;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _acquired;

    public SingleInstanceGuard()
    {
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..16];
        _mutex = new(false, "Local\\RMAutoClicker." + hash);
    }

    public bool TryAcquire()
    {
        try
        {
            _acquired = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _acquired = true;
        }

        if (!_acquired)
        {
            var existing = FindWindow(null, "RM Auto Clicker");
            if (existing != 0)
            {
                ShowWindow(existing, 9);
                SetForegroundWindow(existing);
            }
        }

        return _acquired;
    }

    public void Dispose()
    {
        if (_acquired)
        {
            _acquired = false;
            _mutex.ReleaseMutex();
        }
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
