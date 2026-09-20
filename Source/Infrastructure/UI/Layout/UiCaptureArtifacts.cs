using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace RimMind.Infrastructure.UI.Layout
{
    internal sealed class UiCaptureArtifacts
    {
        public string DirectoryPath { get; }

        public UiCaptureArtifacts(string root, string runId)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Capture root is required.", nameof(root));
            if (string.IsNullOrWhiteSpace(runId) || runId.Length != 32 ||
                !IsHex(runId)) throw new ArgumentException("Capture run ID must be 32 hexadecimal characters.", nameof(runId));

            DirectoryPath = Path.Combine(Path.GetFullPath(root), runId);
            if (Directory.Exists(DirectoryPath)) throw new IOException("Capture output already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(DirectoryPath)!);
            Directory.CreateDirectory(DirectoryPath);
            string marker = Path.Combine(DirectoryPath, ".capture-run");
            using (new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        }

        public async Task<byte[]> WriteImageAsync(string name, byte[] bytes)
        {
            string path = ResolveFile(name, ".png");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            using (var readback = new MemoryStream())
            {
                await stream.CopyToAsync(readback).ConfigureAwait(false);
                return readback.ToArray();
            }
        }

        public async Task WriteManifestAsync(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            string path = ResolveFile("manifest.json", ".json");
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }

        private string ResolveFile(string name, string extension)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Capture artifact name must be a file in the current run.", nameof(name));
            return Path.Combine(DirectoryPath, name);
        }

        private static bool IsHex(string value)
        {
            foreach (char c in value)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
