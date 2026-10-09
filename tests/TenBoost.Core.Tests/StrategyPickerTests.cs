using TenBoost.Core;
using Xunit;

namespace TenBoost.Core.Tests;

public class StrategyPickerTests
{
    private static Strategy S(string id) => new(id, id, "t", ["youtube_tcp", "discord"], ["--new"]);

    private static ProbeResult Result(string id, string[] passed, string[] failed) => new(S(id), passed, failed);

    [Fact]
    public void BestPicksMostTargetsOpened()
    {
        var results = new[]
        {
            Result("a", ["youtube_tcp"], ["discord"]),
            Result("b", ["youtube_tcp", "discord"], []),
            Result("c", [], ["youtube_tcp", "discord"]),
        };

        Assert.Equal("b", StrategyPicker.Best(results)!.Strategy.Id);
    }

    [Fact]
    public void BestBreaksTieByOrder()
    {
        var results = new[]
        {
            Result("a", ["youtube_tcp"], ["discord"]),
            Result("b", ["discord"], ["youtube_tcp"]),
        };

        Assert.Equal("a", StrategyPicker.Best(results)!.Strategy.Id);
    }

    [Fact]
    public void BestIsNullWhenNothingOpened()
    {
        var results = new[] { Result("a", [], ["youtube_tcp", "discord"]) };

        Assert.Null(StrategyPicker.Best(results));
    }

    [Fact]
    public void CoversAllOnlyWhenNoFailures()
    {
        Assert.True(Result("a", ["youtube_tcp", "discord"], []).CoversAll);
        Assert.False(Result("a", ["youtube_tcp"], ["discord"]).CoversAll);
        Assert.False(Result("a", [], []).CoversAll);
    }

    [Fact]
    public void ProbeOrderPutsDefaultFirst()
    {
        var catalog = Catalog.Parse("""
            {"schema":1,"version":"t","default":"b","strategies":[
              {"id":"a","title":"t","source":"t","targets":["discord"],"args":["--new"]},
              {"id":"b","title":"t","source":"t","targets":["discord"],"args":["--new"]},
              {"id":"c","title":"t","source":"t","targets":["discord"],"args":["--new"]}]}
            """);

        Assert.Equal(new[] { "b", "a", "c" }, StrategyPicker.ProbeOrder(catalog).Select(s => s.Id));
    }
}
