using System.Text.RegularExpressions;

namespace TenBoost.Core;

/// <summary>
/// Превращает аргументы стратегии в аргументы winws2. Пути в каталоге задаются только токенами
/// <c>{lua:имя}</c>, <c>{fake:имя}</c>, <c>{filter:имя}</c>, <c>{list:имя}</c> и разрешаются внутри папки установки:
/// каталог не может сослаться ни на что за её пределами.
/// </summary>
public static partial class ArgumentBuilder
{
    [GeneratedRegex(@"\{(?<kind>[a-z]+):(?<name>[^{}]*)\}")]
    private static partial Regex TokenPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex FileNamePattern();

    private static readonly string[] Kinds = ["lua", "fake", "filter", "list"];

    public static void ValidateTemplate(string strategyId, string arg)
    {
        if (!arg.StartsWith("--", StringComparison.Ordinal))
        {
            throw new CatalogException($"{strategyId}: аргумент '{arg}' не является опцией winws2");
        }

        var stripped = TokenPattern().Replace(arg, match =>
        {
            var kind = match.Groups["kind"].Value;
            var name = match.Groups["name"].Value;
            if (!Kinds.Contains(kind))
            {
                throw new CatalogException($"{strategyId}: неизвестный токен '{{{kind}:}}'");
            }

            if (!FileNamePattern().IsMatch(name) || name.Contains("..", StringComparison.Ordinal))
            {
                throw new CatalogException($"{strategyId}: недопустимое имя файла '{name}'");
            }

            return string.Empty;
        });

        if (stripped.Contains('{') || stripped.Contains('}'))
        {
            throw new CatalogException($"{strategyId}: незакрытый или пустой токен в '{arg}'");
        }
    }

    public static IReadOnlyList<string> Build(Strategy strategy, InstallLayout layout) =>
        strategy.Args.Select(arg => TokenPattern().Replace(arg, match => Resolve(match, layout))).ToList();

    /// <summary>Файлы, на которые ссылается стратегия; служба проверяет их наличие до запуска winws2.</summary>
    public static IReadOnlyList<string> ReferencedFiles(Strategy strategy, InstallLayout layout) =>
        strategy.Args
            .SelectMany(arg => TokenPattern().Matches(arg))
            .Select(match => Resolve(match, layout))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string Resolve(Match match, InstallLayout layout)
    {
        var name = match.Groups["name"].Value;
        var dir = match.Groups["kind"].Value switch
        {
            "lua" => layout.LuaDir,
            "fake" => layout.FakeDir,
            "filter" => layout.FilterDir,
            "list" => layout.ListsDir,
            var kind => throw new CatalogException($"неизвестный токен '{kind}'"),
        };
        return Path.Combine(dir, name);
    }
}
