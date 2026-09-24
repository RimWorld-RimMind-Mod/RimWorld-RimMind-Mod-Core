using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Reads Server-Sent Events (SSE) data lines from a stream reader.
    /// Handles the "data: " prefix and "[DONE]" sentinel that are common
    /// across OpenAI and Player2 streaming responses.
    /// </summary>
    internal static class SseStreamReader
    {
        private const string DataPrefix = "data: ";
        private const string DoneSentinel = "[DONE]";

        /// <summary>
        /// Reads SSE data lines and invokes <paramref name="onData"/> for each payload.
        /// Skips empty lines and non-data lines. Stops when "[DONE]" is received,
        /// or throws <see cref="OperationCanceledException"/> if cancellation is requested.
        /// </summary>
        public static async Task ReadDataLinesAsync(
            StreamReader reader,
            Func<string, Task> onData,
            CancellationToken ct)
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(line)) continue;
                if (!line.StartsWith(DataPrefix)) continue;

                string data = line.Substring(DataPrefix.Length);
                if (data == DoneSentinel) break;

                await onData(data);
            }
            ct.ThrowIfCancellationRequested();
        }
    }
}
