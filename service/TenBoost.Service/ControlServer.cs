using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TenBoost.Core;

namespace TenBoost.Service;

/// <summary>Именованный канал для трея: только локальные интерактивные пользователи, только команды из ControlProtocol.</summary>
public sealed class ControlServer(EngineSupervisor supervisor, ILogger<ControlServer> logger) : BackgroundService
{
    private const int MaxInstances = 4;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    ControlProtocol.PipeName,
                    PipeDirection.InOut,
                    MaxInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    ControlProtocol.MaxMessageBytes,
                    ControlProtocol.MaxMessageBytes,
                    CreateSecurity());
            }
            catch (IOException e)
            {
                logger.LogWarning("канал управления: {Error}", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync();
                break;
            }

            _ = Task.Run(() => Serve(pipe, stoppingToken), stoppingToken);
        }
    }

    private static PipeSecurity CreateSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return security;
    }

    private async Task Serve(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        await using var _ = pipe;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(ClientTimeout);
        try
        {
            ControlResponse response;
            try
            {
                var request = ControlProtocol.ParseRequest(await ReadLine(pipe, timeout.Token));
                response = request.Command switch
                {
                    ControlCommand.Enable => supervisor.SetEnabled(true),
                    ControlCommand.Disable => supervisor.SetEnabled(false),
                    ControlCommand.Autopick => supervisor.RequestAutopick(),
                    _ => supervisor.Status(),
                };
            }
            catch (ControlProtocolException e)
            {
                logger.LogWarning("отклонён запрос по каналу: {Error}", e.Message);
                response = supervisor.Status() with { Ok = false, Message = e.Message };
            }

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, ControlProtocol.Json) + "\n");
            await pipe.WriteAsync(bytes, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException)
        {
        }
    }

    private static async Task<string?> ReadLine(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[ControlProtocol.MaxMessageBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length, 1), ct);
            if (read == 0 || buffer[length] == (byte)'\n')
            {
                break;
            }

            length++;
        }

        return length > ControlProtocol.MaxMessageBytes ? null : Encoding.UTF8.GetString(buffer, 0, length);
    }
}
