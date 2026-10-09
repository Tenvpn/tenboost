namespace TenBoost.Core;

/// <summary>Раскладка файлов установки и данных службы. Всё, что запускает служба, лежит только здесь.</summary>
public sealed class InstallLayout
{
    public InstallLayout(string installDir, string dataDir)
    {
        InstallDir = Path.GetFullPath(installDir);
        DataDir = Path.GetFullPath(dataDir);
    }

    public string InstallDir { get; }

    public string DataDir { get; }

    public string EngineDir => Path.Combine(InstallDir, "engine");

    public string Winws2Exe => Path.Combine(EngineDir, "winws2.exe");

    public string EngineVersionFile => Path.Combine(EngineDir, "ENGINE_VERSION");

    public string LuaDir => Path.Combine(EngineDir, "lua");

    public string FakeDir => Path.Combine(EngineDir, "files", "fake");

    public string FilterDir => Path.Combine(EngineDir, "windivert.filter");

    public string ListsDir => Path.Combine(InstallDir, "lists");

    public string CatalogFile => Path.Combine(InstallDir, "strategies", "catalog.json");

    public string LicensesDir => Path.Combine(InstallDir, "licenses");

    public string StateFile => Path.Combine(DataDir, "state.json");

    public string LogsDir => Path.Combine(DataDir, "logs");

    public static InstallLayout ForCurrentProcess() =>
        new(AppContext.BaseDirectory, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TenBoost"));

    public string ReadEngineVersion()
    {
        try
        {
            return File.ReadAllText(EngineVersionFile).Trim();
        }
        catch (IOException)
        {
            return "неизвестно";
        }
        catch (UnauthorizedAccessException)
        {
            return "неизвестно";
        }
    }
}
