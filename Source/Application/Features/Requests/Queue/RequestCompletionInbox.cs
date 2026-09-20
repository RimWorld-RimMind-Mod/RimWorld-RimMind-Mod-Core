using System;
using System.Collections.Concurrent;
using System.Threading;
using RimMind.Application.Common.Interfaces.Async;
using RimMind.Domain.Llm;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Features.Requests.Queue
{
    internal sealed class RequestCompletionInbox
    {
        private readonly ConcurrentQueue<PendingCompletion> _results = new();
        private readonly ConcurrentQueue<(string Message, bool IsWarning)> _logs = new();

        public int PendingCallbackCount => _results.Count;

        public void EnqueueLog(string message, bool isWarning) =>
            _logs.Enqueue((message, isWarning));

        public void Enqueue(
            Result<LlmResponse, RimMindError> result,
            Action<Result<LlmResponse, RimMindError>> callback,
            ICompletionFence fence) =>
            _results.Enqueue(new PendingCompletion(
                result,
                callback,
                fence,
                fence.CancellationToken));

        public void Clear()
        {
            while (_results.TryDequeue(out _)) { }
            while (_logs.TryDequeue(out _)) { }
        }

        public void Drain(Action<string, bool>? logHandler)
        {
            while (_logs.TryDequeue(out var log))
                logHandler?.Invoke(log.Message, log.IsWarning);

            while (_results.TryDequeue(out var completion))
            {
                if (!completion.Fence.TryAcceptCompletion() ||
                    completion.GenerationToken.IsCancellationRequested)
                    continue;

                try
                {
                    completion.Callback(completion.Result);
                }
                catch (Exception exception)
                {
                    logHandler?.Invoke(
                        $"[RimMind-Core] Callback exception: {exception}",
                        true);
                }
            }
        }

        private readonly struct PendingCompletion
        {
            public PendingCompletion(
                Result<LlmResponse, RimMindError> result,
                Action<Result<LlmResponse, RimMindError>> callback,
                ICompletionFence fence,
                CancellationToken generationToken)
            {
                Result = result;
                Callback = callback;
                Fence = fence;
                GenerationToken = generationToken;
            }

            public Result<LlmResponse, RimMindError> Result { get; }
            public Action<Result<LlmResponse, RimMindError>> Callback { get; }
            public ICompletionFence Fence { get; }
            public CancellationToken GenerationToken { get; }
        }
    }
}
