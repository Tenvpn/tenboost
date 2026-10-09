namespace TenBoost.Core;

/// <summary>Итог проверки одной стратегии: какие цели открылись, какие нет.</summary>
public sealed record ProbeResult(Strategy Strategy, IReadOnlyList<string> Passed, IReadOnlyList<string> Failed)
{
    /// <summary>Стратегия открыла все свои цели — дальше перебирать незачем.</summary>
    public bool CoversAll => Failed.Count == 0 && Passed.Count > 0;
}

/// <summary>
/// Чистый выбор стратегии по результатам проверок. Логики сети здесь нет, чтобы её можно было
/// проверить тестами: берём ту, что открыла больше целей; при равенстве — которая раньше в
/// каталоге (поэтому стратегию по умолчанию ставим первой). Если не открылось ничего — null.
/// </summary>
public static class StrategyPicker
{
    public static ProbeResult? Best(IReadOnlyList<ProbeResult> results)
    {
        ProbeResult? best = null;
        foreach (var result in results)
        {
            if (result.Passed.Count == 0)
            {
                continue;
            }

            // Строгое «больше» оставляет первую из равных — порядок каталога решает ничью.
            if (best is null || result.Passed.Count > best.Passed.Count)
            {
                best = result;
            }
        }

        return best;
    }

    /// <summary>Каталог в порядке перебора: стратегия по умолчанию первой, остальные следом.</summary>
    public static IReadOnlyList<Strategy> ProbeOrder(Catalog catalog)
    {
        var order = new List<Strategy>();
        if (catalog.Find(catalog.Default) is { } preferred)
        {
            order.Add(preferred);
        }

        order.AddRange(catalog.Strategies.Where(s => s.Id != catalog.Default));
        return order;
    }
}
