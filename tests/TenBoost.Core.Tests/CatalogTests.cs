using TenBoost.Core;
using Xunit;

namespace TenBoost.Core.Tests;

public class CatalogTests
{
    private static string ShippedCatalog => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "catalog.json"));

    private static string CatalogWith(string args, string targets = "\"discord\"", string id = "s1", string defaultId = "s1") => $$"""
        {"schema":1,"version":"t","default":"{{defaultId}}","strategies":[
          {"id":"{{id}}","title":"t","source":"t","targets":[{{targets}}],"args":[{{args}}]}]}
        """;

    [Fact]
    public void ShippedCatalogIsValid()
    {
        var catalog = Catalog.Parse(ShippedCatalog);

        Assert.NotNull(catalog.Find(catalog.Default));
    }

    [Theory]
    [InlineData("\"--hostlist={list:../../Windows/System32/x}\"")]
    [InlineData("\"--hostlist={list:C:\\\\x.txt}\"")]
    [InlineData("\"--hostlist={list:sub/x.txt}\"")]
    [InlineData("\"--hostlist={list:}\"")]
    [InlineData("\"--lua-init=@{exe:calc.exe}\"")]
    [InlineData("\"--hostlist={list:x.txt\"")]
    [InlineData("\"calc.exe\"")]
    public void RejectsUnsafeArguments(string args)
    {
        Assert.Throws<CatalogException>(() => Catalog.Parse(CatalogWith(args)));
    }

    [Fact]
    public void RejectsUnknownTarget()
    {
        Assert.Throws<CatalogException>(() => Catalog.Parse(CatalogWith("\"--new\"", targets: "\"instagram\"")));
    }

    [Fact]
    public void RejectsMissingDefault()
    {
        Assert.Throws<CatalogException>(() => Catalog.Parse(CatalogWith("\"--new\"", defaultId: "nope")));
    }

    [Fact]
    public void RejectsUnknownSchema()
    {
        Assert.Throws<CatalogException>(() => Catalog.Parse(CatalogWith("\"--new\"").Replace("\"schema\":1", "\"schema\":2")));
    }

    [Fact]
    public void ResolvesTokensInsideInstallDir()
    {
        var layout = new InstallLayout(Path.Combine(Path.GetTempPath(), "tb-install"), Path.Combine(Path.GetTempPath(), "tb-data"));
        var strategy = Catalog.Parse(CatalogWith("\"--lua-init=@{lua:zapret-lib.lua}\",\"--hostlist={list:youtube.txt}\",\"--new\"")).Strategies[0];

        var args = ArgumentBuilder.Build(strategy, layout);

        Assert.Equal(
            [
                "--lua-init=@" + Path.Combine(layout.InstallDir, "engine", "lua", "zapret-lib.lua"),
                "--hostlist=" + Path.Combine(layout.InstallDir, "lists", "youtube.txt"),
                "--new",
            ],
            args);
        Assert.All(ArgumentBuilder.ReferencedFiles(strategy, layout), f => Assert.StartsWith(layout.InstallDir, f));
    }

    [Fact]
    public void ShippedDefaultReferencesExistingRepoFiles()
    {
        var repo = FindRepoRoot();
        var layout = new InstallLayout(repo, Path.GetTempPath());
        var catalog = Catalog.Parse(ShippedCatalog);

        foreach (var file in ArgumentBuilder.ReferencedFiles(catalog.Find(catalog.Default)!, layout))
        {
            var relative = Path.GetRelativePath(repo, file);
            var inRepo = relative.StartsWith("engine", StringComparison.Ordinal)
                ? MapEngineToSubmodule(repo, relative)
                : file;
            Assert.True(File.Exists(inRepo), $"нет файла {relative}");
        }
    }

    private static string MapEngineToSubmodule(string repo, string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar);
        return parts[1] switch
        {
            "windivert.filter" => Path.Combine(repo, "engine", "init.d", "windivert.filter.examples", parts[^1]),
            _ => Path.Combine(repo, relative),
        };
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "bin", "manifest.json")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("корень репозитория не найден");
    }
}
