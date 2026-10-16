using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using TenBoost.Core;

namespace TenBoost.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        VersionText.Text = "Версия " + version.Split('+')[0];
    }

    /// <summary>Папка licenses: THIRD_PARTY.md со списком компонентов, версией движка и ссылками на исходники, тексты лицензий.</summary>
    private void OnLicenses(object sender, RoutedEventArgs e)
    {
        var dir = InstallLayout.ForCurrentProcess().LicensesDir;
        var list = Path.Combine(dir, "THIRD_PARTY.md");
        // Кавычки только вокруг пути: explorer не понимает «"/select,…"» целиком в кавычках.
        Process.Start("explorer.exe", File.Exists(list) ? $"/select,\"{list}\"" : $"\"{dir}\"");
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
