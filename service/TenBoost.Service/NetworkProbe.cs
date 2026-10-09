using System.Net;
using System.Net.Http;

namespace TenBoost.Service;

/// <summary>
/// Проверяет, открылась ли цель, пока применена стратегия. winws2 перехватывает трафик всей
/// системы на уровне драйвера, поэтому запрос отсюда идёт уже через обход. Цель считается
/// «открытой», если короткий HTTPS-запрос к ней прошёл. Это ловит замедление и дурение по DPI;
/// ресурс, закрытый по IP, честно не откроется — тогда цель просто не засчитывается.
/// </summary>
public sealed class NetworkProbe(ILogger<NetworkProbe> logger)
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    // target из каталога → куда стучимся и какой версией HTTP. QUIC проверяем именно по HTTP/3.
    private static readonly IReadOnlyDictionary<string, (string Url, Version Http)> Endpoints =
        new Dictionary<string, (string, Version)>(StringComparer.Ordinal)
        {
            ["youtube_tcp"] = ("https://www.youtube.com/favicon.ico", HttpVersion.Version11),
            ["youtube_quic"] = ("https://www.youtube.com/favicon.ico", HttpVersion.Version30),
            ["discord"] = ("https://discord.com/api/v10/gateway", HttpVersion.Version11),
        };

    public async Task<bool> ProbeAsync(string target, CancellationToken ct)
    {
        if (!Endpoints.TryGetValue(target, out var endpoint))
        {
            logger.LogWarning("нет пробы для цели {Target}", target);
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ProbeTimeout);
        try
        {
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
            using var http = new HttpClient(handler) { Timeout = ProbeTimeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url)
            {
                Version = endpoint.Http,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // Любой валидный ответ (в том числе 4xx) значит, что соединение дошло и не было разорвано DPI.
            return (int)response.StatusCode is > 0 and < 500;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            return false;
        }
    }
}
