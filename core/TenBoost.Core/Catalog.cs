using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenBoost.Core;

public sealed record Strategy(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("targets")] IReadOnlyList<string> Targets,
    [property: JsonPropertyName("args")] IReadOnlyList<string> Args);

public sealed record Catalog(
    [property: JsonPropertyName("schema")] int Schema,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("default")] string Default,
    [property: JsonPropertyName("strategies")] IReadOnlyList<Strategy> Strategies)
{
    public const int SupportedSchema = 1;

    public static readonly IReadOnlySet<string> KnownTargets =
        new HashSet<string>(StringComparer.Ordinal) { "youtube_tcp", "youtube_quic", "discord" };

    public static Catalog Parse(string json)
    {
        var catalog = JsonSerializer.Deserialize<Catalog>(json, new JsonSerializerOptions { AllowTrailingCommas = false })
            ?? throw new CatalogException("каталог пуст");
        catalog.Validate();
        return catalog;
    }

    public static Catalog Load(string path) => Parse(File.ReadAllText(path));

    public Strategy? Find(string id) => Strategies.FirstOrDefault(s => s.Id == id);

    private void Validate()
    {
        if (Schema != SupportedSchema)
        {
            throw new CatalogException($"схема каталога {Schema} не поддерживается, нужна {SupportedSchema}");
        }

        if (Strategies is null || Strategies.Count == 0)
        {
            throw new CatalogException("в каталоге нет стратегий");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var strategy in Strategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.Id) || !ids.Add(strategy.Id))
            {
                throw new CatalogException($"пустой или повторяющийся id стратегии: '{strategy.Id}'");
            }

            if (strategy.Targets is null || strategy.Targets.Count == 0 || strategy.Targets.Any(t => !KnownTargets.Contains(t)))
            {
                throw new CatalogException($"{strategy.Id}: неизвестные цели");
            }

            if (strategy.Args is null || strategy.Args.Count == 0)
            {
                throw new CatalogException($"{strategy.Id}: пустые аргументы");
            }

            foreach (var arg in strategy.Args)
            {
                ArgumentBuilder.ValidateTemplate(strategy.Id, arg);
            }
        }

        if (Find(Default) is null)
        {
            throw new CatalogException($"стратегии по умолчанию '{Default}' нет в каталоге");
        }
    }
}

public sealed class CatalogException(string message) : Exception(message);
