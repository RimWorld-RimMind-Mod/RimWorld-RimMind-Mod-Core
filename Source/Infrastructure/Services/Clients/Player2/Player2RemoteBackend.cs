using System;
using System.Threading;
using System.Threading.Tasks;
using RimMind.Application.Features.Storage;
using RimMind.Domain.Common;
using RimMind.Domain.Storage;
using RimMind.Domain.ValueObjects;
using Newtonsoft.Json;

namespace RimMind.Infrastructure.Services.Clients.Player2
{
    public class Player2RemoteBackend : IRemoteBackend
    {
        private const string DataPathPrefix = "/games/{0}/data/user/";

        public string ProviderName => "Player2";

        private readonly Player2Client _client;
        private readonly string _gameId;
        private readonly string _dataPath;

        public bool IsConfigured => _client.IsConfigured();

        public Player2RemoteBackend(Player2Client client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _gameId = Player2Client.GameClientId;
            _dataPath = string.Format(DataPathPrefix, _gameId);
        }

        public async Task<Result<RemoteEntry?, RimMindError>> PullAsync(string key, CancellationToken ct)
        {
            if (!RemoteKeys.IsValid(key))
                throw new InvalidOperationException($"Key must start with '{RemoteKeys.Prefix}' prefix. Got: {key}");

            try
            {
                var response = await _client.GetRawAsync($"{_dataPath}{Uri.EscapeDataString(key)}");
                if (!response.IsOk)
                    return Result<RemoteEntry?, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("Pull raw request failed"));

                if (response.Content == null)
                    return Result<RemoteEntry?, RimMindError>.Ok(null);

                var entry = new RemoteEntry
                {
                    Key = key,
                    Json = response.Content,
                };
                return Result<RemoteEntry?, RimMindError>.Ok(entry);
            }
            catch (Exception ex)
            {
                return Result<RemoteEntry?, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"PullAsync failed: {ex.Message}", ex));
            }
        }

        public async Task<Result<bool, RimMindError>> PushAsync(string key, string json, long localVersion, CancellationToken ct)
        {
            if (!RemoteKeys.IsValid(key))
                throw new InvalidOperationException($"Key must start with '{RemoteKeys.Prefix}' prefix. Got: {key}");

            try
            {
                var body = new { value = json, version = localVersion };
                string payload = JsonConvert.SerializeObject(body);
                var response = await _client.SendRawAsync($"{_dataPath}{Uri.EscapeDataString(key)}", payload);
                if (!response.IsOk)
                    return Result<bool, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("Push raw request failed"));
                return Result<bool, RimMindError>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"PushAsync failed: {ex.Message}", ex));
            }
        }

        public async Task<Result<bool, RimMindError>> DeleteAsync(string key, CancellationToken ct)
        {
            if (!RemoteKeys.IsValid(key))
                throw new InvalidOperationException($"Key must start with '{RemoteKeys.Prefix}' prefix. Got: {key}");

            try
            {
                var response = await _client.DeleteRawAsync($"{_dataPath}{Uri.EscapeDataString(key)}");
                if (!response.IsOk)
                    return Result<bool, RimMindError>.Err(response.Error ?? RimMindErrors.RemoteBackendFailed("Delete raw request failed"));
                return Result<bool, RimMindError>.Ok(true);
            }
            catch (Exception ex)
            {
                return Result<bool, RimMindError>.Err(RimMindErrors.RemoteBackendFailed($"DeleteAsync failed: {ex.Message}", ex));
            }
        }
    }
}
