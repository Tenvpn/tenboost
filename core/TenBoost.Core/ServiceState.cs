using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenBoost.Core;

/// <summary>Выбор пользователя, который служба переживает через перезагрузку.</summary>
public sealed record ServiceState(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("strategy")] string? StrategyId)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static ServiceState Load(string path, Catalog catalog)
    {
        ServiceState? state = null;
        try
        {
            state = JsonSerializer.Deserialize<ServiceState>(File.ReadAllText(path));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        // Первый запуск до автоподбора: включаемся на стратегии по умолчанию,
        // пользователь (или установщик) запускает «Подобрать» отдельно.
        state ??= new ServiceState(Enabled: true, StrategyId: catalog.Default);
        return state.StrategyId is not null && catalog.Find(state.StrategyId) is not null
            ? state
            : state with { StrategyId = catalog.Default };
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Options));
        File.Move(temp, path, overwrite: true);
    }
}
