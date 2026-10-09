using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using TenBoost.Core;

namespace TenBoost.App;

public partial class AboutWindow : Window
{
    public const string RepositoryUrl = "https://github.com/tenvpn/tenboost";

    public AboutWindow(string? engineVersion)
    {
        InitializeComponent();
        var layout = InstallLayout.ForCurrentProcess();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        VersionText.Text = "Версия " + version.Split('+')[0];
        EngineText.Text = "Движок: " + (engineVersion ?? layout.ReadEngineVersion());
        RepoLink.NavigateUri = new Uri(RepositoryUrl);
        LicenseText.Text = ReadLicense(Path.Combine(layout.LicensesDir, "zapret2-LICENSE.txt"));
    }

    private static string ReadLicense(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "Файл лицензии не найден: " + path;
        }
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
