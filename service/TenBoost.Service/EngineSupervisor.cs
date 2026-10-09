using System.Diagnostics;
using TenBoost.Core;

namespace TenBoost.Service;

/// <summary>
/// Единственный владелец процесса winws2. Команды трея меняют желаемое состояние и будят цикл;
/// запуском, остановкой и перезапуском занимается только цикл.
/// </summary>
public sealed class EngineSupervisor(InstallLayout layout, ILogger<EngineSupervisor> logger) : BackgroundService
{
    private const int MaxFailures = 3;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _wake = new(0);
    private readonly object _gate = new();
    private readonly Queue<DateTime> _failures = new();
    private readonly JobObject _job = new();

    private Catalog? _catalog;
    private ServiceState _desired = new(false, null);
    private Process? _process;
    private EngineState _state = EngineState.Stopped;
    private string? _message;

    public ControlResponse Status()
    {
        lock (_gate)
        {
            return new ControlResponse(true, _desired.Enabled, _state, _desired.StrategyId, layout.ReadEngineVersion(), _message);
        }
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

            try
            {
                _desired.Save(layout.StateFile);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("не удалось сохранить состояние: {Error}", e.Message);
            }
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
            bool enabled;
            EngineState state;
            lock (_gate)
            {
                enabled = _desired.Enabled;
                state = _state;
            }

            if (enabled && _process is null && state != EngineState.Error)
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

        var missing = new[] { layout.Winws2Exe }
            .Concat(ArgumentBuilder.ReferencedFiles(strategy, layout))
            .Where(path => !File.Exists(path))
            .ToList();
        if (missing.Count > 0)
        {
            Fail($"нет файлов движка: {string.Join(", ", missing.Select(Path.GetFileName))}");
            return;
        }

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

        SetState(EngineState.Starting, null);
        try
        {
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => LogEngineLine(e.Data);
            process.ErrorDataReceived += (_, e) => LogEngineLine(e.Data);
            process.Start();
            _job.Assign(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
            logger.LogInformation("winws2 запущен, pid {Pid}, стратегия {Strategy}", process.Id, strategy.Id);
            SetState(EngineState.Running, null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Fail($"winws2 не запустился: {e.Message}");
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
            Fail($"winws2 падает: {MaxFailures} раза за {FailureWindow.TotalMinutes:0} минут. Перезапуск — кнопкой «Включить»");
            return;
        }

        SetState(EngineState.Starting, "перезапуск после сбоя");
        await Task.Delay(RestartDelay, ct);
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }

            logger.LogInformation("winws2 остановлен");
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("остановка winws2: {Error}", e.Message);
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
