using System;
using System.Text;
using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Domain.ValueObjects;
using Newtonsoft.Json;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public partial class Player2Client
    {
        private const int LocalHealthTimeoutSec = 2;
        private const int LocalLoginTimeoutSec = 3;
        private const int DiscoveryPollingDelayMs = 50;

        private static async Task<string?> TryGetLocalPlayer2Key(ILogSink? logSink)
        {
            try
            {
                using (var healthRequest = UnityWebRequest.Get($"{LocalUrl}/v1/health"))
                {
                    healthRequest.timeout = LocalHealthTimeoutSec;
                    var healthOp = healthRequest.SendWebRequest();
                    while (!healthOp.isDone)
                    {
                        if (Current.Game == null) return null;
                        await Task.Delay(DiscoveryPollingDelayMs);
                    }
                    if (healthRequest.result != UnityWebRequest.Result.Success)
                        return null;
                }

                using (var loginRequest = new UnityWebRequest($"{LocalUrl}/v1/login/web/{GameClientId}", "POST"))
                {
                    loginRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{}"));
                    loginRequest.downloadHandler = new DownloadHandlerBuffer();
                    loginRequest.SetRequestHeader("Content-Type", "application/json");
                    loginRequest.timeout = LocalLoginTimeoutSec;

                    var loginOp = loginRequest.SendWebRequest();
                    while (!loginOp.isDone)
                    {
                        if (Current.Game == null) return null;
                        await Task.Delay(DiscoveryPollingDelayMs);
                    }
                    if (loginRequest.result != UnityWebRequest.Result.Success)
                        return null!;

                    var response = JsonConvert.DeserializeObject<Player2LocalLoginResponse>(
                        loginRequest.downloadHandler.text);
                    if (response != null && !string.IsNullOrEmpty(response.P2Key))
                    {
                        logSink?.LogFromBackground("[RimMind-Core] Player2 local app authenticated successfully.");
                        return response.P2Key;
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                logSink?.LogFromBackground($"[RimMind-Core] Local Player2 detection failed: {ex.Message}");
                return null;
            }
        }

        private static void ShowNotification(string messageKey)
        {
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    string text = messageKey.Translate();
                    Messages.Message(text,
                        messageKey.Contains("LocalDetected")
                            ? MessageTypeDefOf.PositiveEvent
                            : MessageTypeDefOf.CautionInput);
                }
                catch (Exception ex) { RimMindErrors.Warn($"[RimMind-Core] Failed to show notification: {ex.Message}"); }
            });
        }
    }
}
