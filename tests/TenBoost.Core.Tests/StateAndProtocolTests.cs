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
    [InlineData("{\"cmd\":\"autopick\"}", ControlCommand.Autopick)]
    public void ParsesKnownCommands(string line, ControlCommand expected)
    {
        Assert.Equal(expected, ControlProtocol.ParseRequest(line).Command);
    }

    [Fact]
    public void SelectCarriesStrategyId()
    {
        var request = ControlProtocol.ParseRequest("{\"cmd\":\"select\",\"strategy\":\"discord_md5_disorder\"}");

        Assert.Equal(new ControlRequest(ControlCommand.Select, "discord_md5_disorder"), request);
    }

    [Fact]
    public void PickingResponseRoundTrips()
    {
        var response = new ControlResponse(true, true, EngineState.Picking, "a", "v", "подбор", new PickProgress(1, 3, "t"));

        var json = System.Text.Json.JsonSerializer.Serialize(response, ControlProtocol.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ControlResponse>(json, ControlProtocol.Json);

        Assert.Equal(response, back);
        Assert.Contains("\"picking\"", json);
    }

    [Fact]
    public void StatusWithoutPickHasNullProgress()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new ControlResponse(true, false, EngineState.Stopped, null, "v", null), ControlProtocol.Json);

        Assert.Null(System.Text.Json.JsonSerializer.Deserialize<ControlResponse>(json, ControlProtocol.Json)!.Pick);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"cmd\":\"run\",\"args\":\"calc.exe\"}")]
    [InlineData("{\"cmd\":7}")]
    [InlineData("not json")]
    [InlineData("{\"cmd\":\"select\"}")]
    [InlineData("{\"cmd\":\"select\",\"strategy\":\"\"}")]
    [InlineData("{\"cmd\":\"select\",\"strategy\":\"--wf-tcp=1 calc\"}")]
    public void RejectsUnknownCommands(string? line)
    {
        Assert.Throws<ControlProtocolException>(() => ControlProtocol.ParseRequest(line));
    }
}
