using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Common.Models;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimWorld;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client
    {
        private const int RawRequestTimeoutSec = RimMindDefaults.Player2RawRequestTimeout;
        private const int RawRequestPollingDelayMs = RimMindDefaults.Player2RawRequestPollingDelay;

        public async Task<RawResponse> SendRawAsync(string path, string jsonBody)
        {
            return await SendRawRequestAsync(path, "POST", jsonBody);
        }

        public async Task<RawResponse> GetRawAsync(string path)
        {
            return await SendRawRequestAsync(path, "GET", null);
        }

        public async Task<RawResponse> DeleteRawAsync(string path)
        {
            return await SendRawRequestAsync(path, "DELETE", null);
        }

        private async Task<RawResponse> SendRawRequestAsync(string path, string method, string? jsonBody)
        {
            string endpoint = $"{CurrentApiUrl}{path}";
            try
            {
                if (Current.Game == null)
                    return RawResponse.Err(RimMindErrors.ClientTransient("Game exiting"));

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RawRequestTimeoutSec));
                using var request = new HttpRequestMessage(new HttpMethod(method), endpoint);
                if (jsonBody != null)
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                else if (method == "POST")
                    request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
                request.Headers.TryAddWithoutValidation("player2-game-key", GameClientId);

                using var response = await SharedHttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                string? content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                    return RawResponse.Ok(content);
                return RawResponse.Err(RimMindErrors.ClientTransient(response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}"));
            }
            catch (Exception ex) { return RawResponse.Err(RimMindErrors.ClientTransient(ex.Message, ex)); }
        }
    }

    public class RawResponse
    {
        private readonly Result<string?, RimMindError> _result;

        public string? Content => _result.TryGetValue(out var value) ? value : null;
        public bool IsOk => _result.IsOk;
        public RimMindError? Error => _result.TryGetError(out var err) ? err : null;

        private RawResponse(Result<string?, RimMindError> result)
        {
            _result = result;
        }

        public static RawResponse Ok(string? content) => new RawResponse(Result<string?, RimMindError>.Ok(content));
        public static RawResponse Err(RimMindError error) => new RawResponse(Result<string?, RimMindError>.Err(error));
    }
}
