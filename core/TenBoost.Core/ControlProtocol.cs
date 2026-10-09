using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenBoost.Core;

/// <summary>
/// Канал трей → служба. Одна строка JSON в каждую сторону. Список команд закрыт:
/// аргументы winws2 по каналу не передаются никогда, иначе любой пользователь запускал бы через службу что угодно от SYSTEM.
/// </summary>
public static class ControlProtocol
{
    public const string PipeName = "TenBoost.Control";

    public const int MaxMessageBytes = 4096;

    public static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
    };

    public static ControlRequest ParseRequest(string? line)
    {
        if (string.IsNullOrEmpty(line) || line.Length > MaxMessageBytes)
        {
            throw new ControlProtocolException("пустой или слишком длинный запрос");
        }

        try
        {
            return JsonSerializer.Deserialize<ControlRequest>(line, Json)
                ?? throw new ControlProtocolException("пустой запрос");
        }
        catch (JsonException)
        {
            throw new ControlProtocolException("неизвестная команда");
        }
    }
}

public enum ControlCommand
{
    Status,
    Enable,
    Disable,
    Autopick,
}

public sealed record ControlRequest([property: JsonPropertyName("cmd")] ControlCommand Command);

public enum EngineState
{
    Stopped,
    Starting,
    Running,
    Error,
    Picking,
}

/// <summary>Прогресс автоподбора для трея: «проверяю N из M».</summary>
public sealed record PickProgress(
    [property: JsonPropertyName("done")] int Done,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("current")] string? Current);

public sealed record ControlResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("state")] EngineState State,
    [property: JsonPropertyName("strategy")] string? StrategyId,
    [property: JsonPropertyName("engine")] string EngineVersion,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("pick")] PickProgress? Pick = null);

public sealed class ControlProtocolException(string message) : Exception(message);
