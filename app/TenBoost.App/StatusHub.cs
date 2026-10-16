using System.Windows.Threading;
using TenBoost.Core;

namespace TenBoost.App;

/// <summary>
/// Одно состояние службы на трей и окно: опрашивает службу и шлёт команды.
/// Пока окно открыто или идёт подбор, опрос чаще — человек смотрит на экран.
/// </summary>
internal sealed class StatusHub : IDisposable
{
    private static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FastPoll = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private bool _watching;

    public StatusHub()
    {
        _timer = new DispatcherTimer { Interval = SlowPoll };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public event Action? Changed;

    public ControlResponse? Last { get; private set; }

    public bool Busy { get; private set; }

    public bool Picking => Last is { State: EngineState.Picking };

    /// <summary>Окно на экране: опрашиваем чаще.</summary>
    public bool Watching
    {
        get => _watching;
        set
        {
            _watching = value;
            UpdateInterval();
            if (value)
            {
                _ = RefreshAsync();
            }
        }
    }

    public void Dispose() => _timer.Stop();

    public Task ToggleAsync() =>
        Last is null ? Task.CompletedTask : SendAsync(Last.Enabled ? ControlCommand.Disable : ControlCommand.Enable);

    // Служба подтверждает старт подбора; дальше прогресс приходит обычным опросом Status.
    public Task AutopickAsync() => Last is null ? Task.CompletedTask : SendAsync(ControlCommand.Autopick);

    private async Task RefreshAsync()
    {
        if (Busy)
        {
            return;
        }

        Apply(await ControlClient.SendAsync(ControlCommand.Status));
    }

    private async Task SendAsync(ControlCommand command)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        Changed?.Invoke();
        try
        {
            Apply(await ControlClient.SendAsync(command));
        }
        finally
        {
            Busy = false;
            Changed?.Invoke();
        }
    }

    private void Apply(ControlResponse? status)
    {
        Last = status;
        UpdateInterval();
        Changed?.Invoke();
    }

    private void UpdateInterval() => _timer.Interval = _watching || Picking ? FastPoll : SlowPoll;
}
