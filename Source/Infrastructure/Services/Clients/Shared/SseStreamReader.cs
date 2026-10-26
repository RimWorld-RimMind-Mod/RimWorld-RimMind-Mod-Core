using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RimMind.Infrastructure.Services.Clients.Shared
{
    /// <summary>
    /// Reads Server-Sent Events (SSE) data lines from a stream reader.
    /// Handles the "data:" field (with or without the optional single space, per the SSE spec)
    /// and the "[DONE]" sentinel that are common across OpenAI and Player2 streaming responses.
    /// </summary>
    internal static class SseStreamReader
    {
        private const string DataField = "data:";
        private const string DoneSentinel = "[DONE]";

        /// <summary>
        /// Extracts the payload of an SSE <c>data:</c> line. Returns false for blank lines,
        /// comments and other fields (<c>event:</c>, <c>id:</c>, <c>retry:</c>).
        /// </summary>
        public static bool TryParseDataLine(string? line, out string data)
        {
            data = string.Empty;
            if (string.IsNullOrEmpty(line)) return false;
            if (!line!.StartsWith(DataField, StringComparison.Ordinal)) return false;

            data = line.Substring(DataField.Length);
            if (data.Length > 0 && data[0] == ' ')
                data = data.Substring(1);
            return true;
        }

        /// <summary>
        /// Reads SSE data lines and invokes <paramref name="onData"/> for each payload.
        /// Skips empty lines and non-data lines. Stops when "[DONE]" is received,
        /// or throws <see cref="OperationCanceledException"/> if cancellation is requested.
        /// <para>
        /// <see cref="StreamReader.ReadLineAsync()"/> is not cancellable, so callers must also arrange for
        /// the underlying response to be disposed when <paramref name="ct"/> fires
        /// (see <c>HttpTransport.OpenStreamAsync</c>). The resulting I/O failure is surfaced
        /// here as an <see cref="OperationCanceledException"/>.
        /// </para>
        /// </summary>
        /// <param name="onActivity">Invoked after every received line; used to re-arm idle timeouts.</param>
        public static async Task ReadDataLinesAsync(
            StreamReader reader,
            Func<string, Task> onData,
            CancellationToken ct,
            Action? onActivity = null)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                string? line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ct.IsCancellationRequested && (ex is ObjectDisposedException || ex is IOException || ex is System.Net.WebException))
                {
                    throw new OperationCanceledException("SSE stream aborted by cancellation.", ex, ct);
                }

                if (line == null) break;
                onActivity?.Invoke();

                if (!TryParseDataLine(line, out string data)) continue;
                if (data == DoneSentinel) break;

                await onData(data).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
        }
    }
}
