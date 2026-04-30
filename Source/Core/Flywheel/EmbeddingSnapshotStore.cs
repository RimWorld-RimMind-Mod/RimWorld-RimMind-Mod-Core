using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using RimMind.Core.Context;
using RimMind.Core.Settings;
using Verse;

namespace RimMind.Core.Flywheel
{
    public class EmbeddingSnapshotRecord
    {
        public string NpcId = null!;
        public string ScenarioId = null!;
        public string Key = null!;
        public string Layer = null!;
        public string SourceText = null!;
        public float[] Vector = null!;
        public float RelevanceScore;
        public long TimestampTicks;
    }

    public class EmbeddingSnapshotStore
    {
        private const int FlushThreshold = 20;

        private readonly List<EmbeddingSnapshotRecord> _buffer = new List<EmbeddingSnapshotRecord>();
        private readonly object _lock = new object();

        public void Record(EmbeddingSnapshotRecord record)
        {
            lock (_lock)
            {
                _buffer.Add(record);
                if (_buffer.Count >= FlushThreshold)
                    FlushInternal();
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                FlushInternal();
            }
        }

        private void FlushInternal()
        {
            if (_buffer.Count == 0) return;

            var toWrite = new List<EmbeddingSnapshotRecord>(_buffer);
            _buffer.Clear();

            try
            {
                string? settingsPath = RimMindCoreMod.Settings?.embeddingSnapshotPath;
                string dir = !string.IsNullOrWhiteSpace(settingsPath)
                    ? settingsPath!
                    : Path.Combine(GenFilePaths.SaveDataFolderPath, "Telemetry", "Embedding");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string fileName = $"{DateTime.Now:yyyy-MM-dd}.jsonl";
                string filePath = Path.Combine(dir, fileName);

                var lines = new List<string>(toWrite.Count);
                foreach (var record in toWrite)
                    lines.Add(JsonConvert.SerializeObject(record, Formatting.None));

                File.AppendAllLines(filePath, lines);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimMind] EmbeddingSnapshot flush failed: {ex.Message}");
            }
        }
    }
}
