using TenBoost.Core;
using Xunit;

namespace TenBoost.Core.Tests;

public class StateAndProtocolTests
{
    private static readonly Catalog Catalog = Catalog.Parse("""
        {"schema":1,"version":"t","default":"a","strategies":[
          {"id":"a","title":"t","source":"t","targets":["discord"],"args":["--new"]},
          {"id":"b","title":"t","source":"t","targets":["discord"],"args":["--new"]}]}
        """);

    [Fact]
    public void MissingStateFallsBackToEnabledDefault()
    {
        var state = ServiceState.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "state.json"), Catalog);

        Assert.Equal(new ServiceState(true, "a"), state);
    }

    [Fact]
    public void StateRoundTripsAndDropsUnknownStrategy()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "state.json");

        new ServiceState(false, "b").Save(path);
        Assert.Equal(new ServiceState(false, "b"), ServiceState.Load(path, Catalog));

        new ServiceState(false, "gone").Save(path);
        Assert.Equal(new ServiceState(false, "a"), ServiceState.Load(path, Catalog));
    }

    [Theory]
    [InlineData("{\"cmd\":\"status\"}", ControlCommand.Status)]
    [InlineData("{\"cmd\":\"enable\"}", ControlCommand.Enable)]
    [InlineData("{\"cmd\":\"disable\"}", ControlCommand.Disable)]
    public void ParsesKnownCommands(string line, ControlCommand expected)
    {
        Assert.Equal(expected, ControlProtocol.ParseRequest(line).Command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"cmd\":\"run\",\"args\":\"calc.exe\"}")]
    [InlineData("{\"cmd\":7}")]
    [InlineData("not json")]
    public void RejectsUnknownCommands(string? line)
    {
        Assert.Throws<ControlProtocolException>(() => ControlProtocol.ParseRequest(line));
    }
}
