using System.Diagnostics;
using TenBoost.Core;

namespace TenBoost.Service;

/// <summary>
/// Единственный владелец процесса winws2. Команды трея меняют желаемое состояние и будят цикл;
/// запуском, остановкой и перезапуском занимается только цикл.
/// </summary>
public sealed class EngineSupervisor(InstallLayout layout, NetworkProbe probe, ILogger<EngineSupervisor> logger) : BackgroundService
{
    private const int MaxFailures = 3;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(3);
    // Сколько ждём, пока winws2 поднимет драйвер и начнёт перехват, прежде чем проверять цель.
    private static readonly TimeSpan ProbeWarmup = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private readonly Queue<DateTime> _failures = new();
    private readonly JobObject _job = new();

    private Catalog? _catalog;
    private ServiceState _desired = new(false, null);
    private Process? _process;
    private EngineState _state = EngineState.Stopped;
    private string? _message;
    private bool _autopick;
    private bool _restart;
    private PickProgress? _pick;

    public ControlResponse Status()
    {
        lock (_gate)
        {
            return new ControlResponse(true, _desired.Enabled, _state, _desired.StrategyId, layout.ReadEngineVersion(), _message, _pick);
        }
    }

    public ControlResponse RequestAutopick()
    {
        lock (_gate)
        {
            if (_catalog is null)
            {
                return Status() with { Ok = false };
            }

            _autopick = true;
        }

        logger.LogInformation("команда: подобрать стратегию");
        _wake.Release();
        return Status();
    }

    /// <summary>Ручной выбор: только id из своего каталога, аргументы по каналу не приходят.</summary>
    public ControlResponse SelectStrategy(string id)
    {
        lock (_gate)
        {
            if (_catalog?.Find(id) is null || _state == EngineState.Picking)
            {
                return Status() with { Ok = false };
            }

            _desired = new ServiceState(Enabled: true, StrategyId: id);
            _failures.Clear();
            if (_state == EngineState.Error)
            {
                _state = EngineState.Stopped;
                _message = null;
            }

            _restart = true;
            TrySaveState();
        }

        logger.LogInformation("команда: выбрать стратегию {Strategy}", id);
        _wake.Release();
        return Status();
    }

    public ControlResponse SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_catalog is null)
            {
                return Status() with { Ok = false };
            }

            _desired = _desired with { Enabled = enabled };
            if (enabled && _state == EngineState.Error)
            {
                _failures.Clear();
                _state = EngineState.Stopped;
                _message = null;
            }

            TrySaveState();
        }

        logger.LogInformation("команда: {Command}", enabled ? "включить" : "выключить");
        _wake.Release();
        return Status();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("служба запущена, движок {Engine}, папка {Dir}", layout.ReadEngineVersion(), layout.InstallDir);
        try
        {
            var catalog = Catalog.Load(layout.CatalogFile);
            lock (_gate)
            {
                _catalog = catalog;
                _desired = ServiceState.Load(layout.StateFile, catalog);
            }

            logger.LogInformation("каталог {Version}, стратегия {Strategy}, включено: {Enabled}", catalog.Version, _desired.StrategyId, _desired.Enabled);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CatalogException or System.Text.Json.JsonException)
        {
            Fail($"каталог стратегий не загружен: {e.Message}");
            return;
        }

        try
        {
            await RunLoop(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            StopProcess();
            logger.LogInformation("служба остановлена");
        }
    }

    public override void Dispose()
    {
        StopProcess();
        _job.Dispose();
        _wake.Dispose();
        base.Dispose();
    }

    private async Task RunLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool enabled, autopick, restart;
            EngineState state;
            lock (_gate)
            {
                enabled = _desired.Enabled;
                state = _state;
                autopick = _autopick;
                _autopick = false;
                restart = _restart;
                _restart = false;
            }

            if (autopick)
            {
                await PickAsync(ct);
                continue;
            }

            // Сменили стратегию руками: снимаем движок, ниже он поднимется уже на новой.
            if (restart && _process is not null)
            {
                StopProcess();
                SetState(EngineState.Stopped, null);
                state = EngineState.Stopped;
            }

            if (enabled && _process is null && state is not (EngineState.Error or EngineState.Picking))
            {
                TryStart();
            }
            else if (!enabled && _process is not null)
            {
                StopProcess();
                SetState(EngineState.Stopped, null);
            }

            var exited = _process?.WaitForExitAsync(ct) ?? Task.Delay(Timeout.Infinite, ct);
            await Task.WhenAny(exited, _wake.WaitAsync(ct));
            ct.ThrowIfCancellationRequested();

            if (_process is { HasExited: true } process)
            {
                await OnUnexpectedExit(process, ct);
            }
        }
    }

    private void TryStart()
    {
        Strategy strategy;
        lock (_gate)
        {
            strategy = _catalog!.Find(_desired.StrategyId!)!;
        }

        if (MissingFiles(strategy) is { Count: > 0 } missing)
        {
            Fail($"нет файлов движка: {string.Join(", ", missing.Select(Path.GetFileName))}");
            return;
        }

        SetState(EngineState.Starting, null);
        try
        {
            _process = LaunchEngine(strategy);
            logger.LogInformation("winws2 запущен, pid {Pid}, стратегия {Strategy}", _process.Id, strategy.Id);
            SetState(EngineState.Running, null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Fail($"движок не запустился: {e.Message}");
        }
    }

    private IReadOnlyList<string> MissingFiles(Strategy strategy) =>
        new[] { layout.Winws2Exe }
            .Concat(ArgumentBuilder.ReferencedFiles(strategy, layout))
            .Where(path => !File.Exists(path))
            .ToList();

    /// <summary>Запускает winws2 с одной стратегией и возвращает процесс. Владение процессом — на вызывающем.</summary>
    private Process LaunchEngine(Strategy strategy)
    {
        StaleDriver.RemoveIfBroken(logger);
        var startInfo = new ProcessStartInfo
        {
            FileName = layout.Winws2Exe,
            WorkingDirectory = layout.EngineDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in ArgumentBuilder.Build(strategy, layout))
        {
            startInfo.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => LogEngineLine(e.Data);
        process.ErrorDataReceived += (_, e) => LogEngineLine(e.Data);
        process.Start();
        _job.Assign(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    /// <summary>Перебирает стратегии каталога, выбирает ту, что открыла больше целей, и запоминает её.</summary>
    private async Task PickAsync(CancellationToken ct)
    {
        StopProcess();
        Catalog catalog;
        lock (_gate)
        {
            catalog = _catalog!;
            _failures.Clear();
        }

        var order = StrategyPicker.ProbeOrder(catalog);
        var results = new List<ProbeResult>();
        logger.LogInformation("подбор: {Count} стратегий", order.Count);
        SetState(EngineState.Picking, "подбор стратегии…");

        try
        {
            for (var i = 0; i < order.Count; i++)
            {
                var strategy = order[i];
                SetPick(new PickProgress(i, order.Count, strategy.Title));
                var result = await ProbeStrategy(strategy, ct);
                results.Add(result);
                logger.LogInformation("подбор: {Id} — открыто {Passed}/{Total}, не открылось: {Failed}",
                    strategy.Id, result.Passed.Count, result.Passed.Count + result.Failed.Count,
                    result.Failed.Count == 0 ? "—" : string.Join(", ", result.Failed));
                if (result.CoversAll)
                {
                    break;
                }
            }
        }
        finally
        {
            SetPick(null);
        }

        var best = StrategyPicker.Best(results);
        var chosen = best?.Strategy.Id ?? catalog.Default;
        var message = best is null
            ? "ни одна стратегия не открыла цели — провайдер режет не по DPI; ставлю стратегию по умолчанию"
            : $"подобрана «{best.Strategy.Title}»: открыто {best.Passed.Count} из {best.Passed.Count + best.Failed.Count}";
        logger.LogInformation("подбор завершён: {Chosen}", chosen);

        lock (_gate)
        {
            _desired = _desired with { Enabled = true, StrategyId = chosen };
            TrySaveState();
        }

        // Стратегия выбрана — следующий оборот цикла её запустит.
        SetState(EngineState.Stopped, message);
    }

    private async Task<ProbeResult> ProbeStrategy(Strategy strategy, CancellationToken ct)
    {
        if (MissingFiles(strategy).Count > 0)
        {
            return new ProbeResult(strategy, [], strategy.Targets.ToList());
        }

        Process? process = null;
        try
        {
            process = LaunchEngine(strategy);
            await Task.Delay(ProbeWarmup, ct);

            var passed = new List<string>();
            var failed = new List<string>();
            foreach (var target in strategy.Targets)
            {
                switch (await probe.ProbeAsync(target, ct))
                {
                    case true:
                        passed.Add(target);
                        break;
                    case false:
                        failed.Add(target);
                        break;
                }
            }

            return new ProbeResult(strategy, passed, failed);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning("проба {Id}: {Error}", strategy.Id, e.Message);
            return new ProbeResult(strategy, [], strategy.Targets.ToList());
        }
        finally
        {
            KillProcess(process);
        }
    }

    private void SetPick(PickProgress? pick)
    {
        lock (_gate)
        {
            _pick = pick;
        }
    }

    private void TrySaveState()
    {
        try
        {
            _desired.Save(layout.StateFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("не удалось сохранить состояние: {Error}", e.Message);
        }
    }

    private async Task OnUnexpectedExit(Process process, CancellationToken ct)
    {
        logger.LogWarning("winws2 завершился с кодом {Code}", process.ExitCode);
        process.Dispose();
        _process = null;

        var now = DateTime.UtcNow;
        _failures.Enqueue(now);
        while (_failures.Count > 0 && now - _failures.Peek() > FailureWindow)
        {
            _failures.Dequeue();
        }

        if (_failures.Count >= MaxFailures)
        {
            Fail($"движок упал {MaxFailures} раза за {FailureWindow.TotalMinutes:0} минут");
            return;
        }

        SetState(EngineState.Starting, "перезапуск после сбоя");
        await Task.Delay(RestartDelay, ct);
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        if (KillProcess(process))
        {
            logger.LogInformation("winws2 остановлен");
        }
    }

    /// <summary>Снимает процесс winws2 вместе с детьми. true — если процесс был и снят без исключения.</summary>
    private bool KillProcess(Process? process)
    {
        if (process is null)
        {
            return false;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }

            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("остановка winws2: {Error}", e.Message);
            return false;
        }
        finally
        {
            process.Dispose();
        }
    }

    private void Fail(string message)
    {
        logger.LogError("{Message}", message);
        SetState(EngineState.Error, message);
    }

    private void SetState(EngineState state, string? message)
    {
        lock (_gate)
        {
            _state = state;
            _message = message;
        }
    }

    private void LogEngineLine(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            logger.LogInformation("winws2: {Line}", line);
        }
    }
}
