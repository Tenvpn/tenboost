using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TenBoost.Service;

/// <summary>
/// Чужая мёртвая регистрация драйвера WinDivert. Ручной zapret регистрирует службу «WinDivert» со своим путём к .sys;
/// когда папку удалили или антивирус унёс файл, запись остаётся, и WinDivert.dll открывает её вместо нашей:
/// winws2 падает с «error opening filter: The system cannot find the file specified».
/// Удаляем запись, только если файла по её пути нет — живой драйвер другой программы не трогаем.
/// </summary>
internal static class StaleDriver
{
    private const string ServiceName = "WinDivert";
    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\" + ServiceName;

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint Delete = 0x00010000;
    private const uint ServiceStopped = 0x00000001;

    public static void RemoveIfBroken(ILogger logger)
    {
        string? imagePath;
        using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey))
        {
            imagePath = key?.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        if (imagePath is null || File.Exists(ResolveDriverPath(imagePath)))
        {
            return;
        }

        try
        {
            Remove();
            logger.LogWarning("удалена мёртвая регистрация драйвера WinDivert: файла {Path} нет", imagePath);
        }
        catch (Win32Exception e)
        {
            logger.LogWarning("мёртвая регистрация драйвера WinDivert ({Path}) не удалена: {Error}", imagePath, e.Message);
        }
    }

    /// <summary>ImagePath драйвера: «\??\C:\…», «\SystemRoot\…», «system32\drivers\…» или обычный путь.</summary>
    internal static string ResolveDriverPath(string imagePath)
    {
        var path = Environment.ExpandEnvironmentVariables(imagePath.Trim().Trim('"'));
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return path[4..];
        }

        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(windows, path[12..]);
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(windows, path);
    }

    private static void Remove()
    {
        var manager = OpenSCManagerW(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = OpenServiceW(manager, ServiceName, ServiceQueryStatus | Delete);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!QueryServiceStatus(service, out var status))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                // Загруженный драйвер без файла на диске всё равно работает — не наш случай, не трогаем.
                if (status.CurrentState != ServiceStopped)
                {
                    throw new Win32Exception(0, "драйвер загружен");
                }

                if (!DeleteService(service))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr service);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }
}
