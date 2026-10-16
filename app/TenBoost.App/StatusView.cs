using System.Drawing;
using TenBoost.Core;

namespace TenBoost.App;

/// <summary>Что показать человеку при данном ответе службы. Голос по брендбуку: спокойно, без восклицаний, сначала что случилось, потом что делать.</summary>
internal enum Tone
{
    Ok,
    Off,
    Progress,
    Warn,
    Bad,
}

internal enum Scene
{
    Off,
    Work,
    Search,
    Error,
}

internal sealed record StatusView(Tone Tone, Scene Scene, string Badge, string Title, string Hint, string? Detail, string Short)
{
    public Color Dot => Tone switch
    {
        Tone.Ok or Tone.Progress => Color.FromArgb(0xB6, 0xE3, 0x2E),
        Tone.Warn => Color.FromArgb(0xFF, 0xB2, 0x45),
        Tone.Bad => Color.FromArgb(0xFF, 0x6B, 0x5E),
        _ => Color.FromArgb(0x8A, 0x96, 0x8D),
    };

    public static StatusView From(ControlResponse? status) => status switch
    {
        null => new(Tone.Bad, Scene.Error, "Нет связи", "Служба не отвечает",
            "Перезагрузите компьютер. Если не поможет, переустановите TenBoost", null, "служба не отвечает"),
        { State: EngineState.Picking } => new(Tone.Progress, Scene.Search, "Подбор", "Подбираю настройку",
            PickHint(status.Pick), null, PickShort(status.Pick)),
        { Enabled: false } => new(Tone.Off, Scene.Off, "Выключено", "Ускорение выключено",
            "YouTube и Discord идут напрямую, как без TenBoost", status.Message, "выключено"),
        { State: EngineState.Running } => new(Tone.Ok, Scene.Work, "Работает", "Ускорение включено",
            "YouTube и Discord идут через TenBoost", null, "включено"),
        { State: EngineState.Error } => new(Tone.Bad, Scene.Error, "Ошибка", "Не запустилось",
            "Включите ещё раз или подберите настройку заново", status.Message, "ошибка: " + status.Message),
        _ => new(Tone.Warn, Scene.Work, "Запуск", "Запускаю",
            "Это займёт пару секунд", status.Message, "запускается"),
    };

    private static string PickHint(PickProgress? pick) =>
        pick is null ? "Проверяю, что открывается" : $"Проверяю {pick.Done + 1} из {pick.Total}: {pick.Current}";

    private static string PickShort(PickProgress? pick) =>
        pick is null ? "подбор…" : $"подбор {pick.Done + 1} из {pick.Total}";
}
