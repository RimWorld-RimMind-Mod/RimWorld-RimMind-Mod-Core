using System;
using System.Text;
using System.Threading.Tasks;
using RimMind.Application.Common.Models;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;
using RimWorld;
using UnityEngine.Networking;
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
                using var webRequest = new UnityWebRequest(endpoint, method);
                if (jsonBody != null)
                    webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                else if (method == "POST")
                    webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                webRequest.SetRequestHeader("player2-game-key", GameClientId);
                webRequest.timeout = RawRequestTimeoutSec;

                var asyncOp = webRequest.SendWebRequest();
                while (!asyncOp.isDone) { if (Current.Game == null) { return RawResponse.Err(RimMindErrors.ClientTransient("Game exiting")); } await Task.Delay(RawRequestPollingDelayMs); }

                string? content = webRequest.downloadHandler?.text;
                bool ok = webRequest.result != UnityWebRequest.Result.ConnectionError
                          && webRequest.result != UnityWebRequest.Result.ProtocolError;
                if (ok) return RawResponse.Ok(content);
                return RawResponse.Err(RimMindErrors.ClientTransient(webRequest.error));
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
