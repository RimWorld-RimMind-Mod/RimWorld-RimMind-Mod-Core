using System.Collections.Concurrent;
using RimMind.Application.Common.Interfaces.Abstractions;
using RimMind.Application.Common.Interfaces.Internal;
using RimMind.Domain.ValueObjects;
using Verse;

namespace RimMind.Infrastructure.Services.Verse
{
    public sealed class VerseLogSink : ILogSink
    {
        private const string Prefix = "[RimMind-Core]";
        private const int MaxPendingBackgroundLogs = 256;

        public const int LevelMessage = 0;
        public const int LevelWarning = 1;
        public const int LevelError = 2;

        private static readonly ConcurrentQueue<(string Message, int Level)> PendingBackgroundLogs =
            new ConcurrentQueue<(string Message, int Level)>();

        private static string Format(string msg)
        {
            var trace = TraceContext.Current;
            return trace != null ? $"{Prefix}[trace={trace}] {msg}" : $"{Prefix} {msg}";
        }

        public static void EnqueueOrLog(string formattedMessage, int level)
        {
            if (string.IsNullOrEmpty(formattedMessage))
                return;

            try
            {
                if (UnityData.IsInMainThread)
                {
                    FlushPendingOnMainThread();
                    WriteDirectlyOnMainThread(formattedMessage, level);
                    return;
                }
            }
            catch
            {
                // If UnityData is unavailable in unit tests, fall through to queue
            }

            while (PendingBackgroundLogs.Count >= MaxPendingBackgroundLogs && PendingBackgroundLogs.TryDequeue(out _))
            {
            }
            PendingBackgroundLogs.Enqueue((formattedMessage, level));
        }

        public static void FlushPendingOnMainThread()
        {
            try
            {
                if (!UnityData.IsInMainThread)
                    return;

                int flushed = 0;
                while (flushed < 64 && PendingBackgroundLogs.TryDequeue(out var item))
                {
                    WriteDirectlyOnMainThread(item.Message, item.Level);
                    flushed++;
                }
            }
            catch
            {
            }
        }

        private static void WriteDirectlyOnMainThread(string formattedMessage, int level)
        {
            global::Verse.Log.ResetMessageCount();
            switch (level)
            {
                case LevelError:
                    global::Verse.Log.Error(formattedMessage);
                    break;
                case LevelWarning:
                    global::Verse.Log.Warning(formattedMessage);
                    break;
                default:
                    global::Verse.Log.Message(formattedMessage);
                    break;
            }
        }

        public void Message(string msg) => EnqueueOrLog(Format(msg), LevelMessage);
        public void Warning(string msg) => EnqueueOrLog(Format(msg), LevelWarning);
        public void Error(string msg) => EnqueueOrLog(Format(msg), LevelError);

        public void LogFromBackground(string msg, bool isWarning = false)
        {
            EnqueueOrLog(Format(msg), isWarning ? LevelWarning : LevelMessage);
        }
    }
}
