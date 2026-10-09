using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using TenBoost.Core;

namespace TenBoost.App;

/// <summary>Клиент канала службы. Трей работает без прав администратора и всё привилегированное просит у службы.</summary>
internal static class ControlClient
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<ControlResponse?> SendAsync(ControlCommand command)
    {
        using var cts = new CancellationTokenSource(Timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ControlProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token);
            var request = JsonSerializer.Serialize(new ControlRequest(command), ControlProtocol.Json) + "\n";
            await pipe.WriteAsync(Encoding.UTF8.GetBytes(request), cts.Token);
            await pipe.FlushAsync(cts.Token);

            using var reader = new StreamReader(pipe, Encoding.UTF8);
            var line = await reader.ReadLineAsync(cts.Token);
            return line is null ? null : JsonSerializer.Deserialize<ControlResponse>(line, ControlProtocol.Json);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
