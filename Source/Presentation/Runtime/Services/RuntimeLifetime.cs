using System;
using System.Threading;
using RimMind.Application.Common.Interfaces.Async;

namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeLifetime : ICompletionFence, IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;
        private readonly Func<RuntimeGenerationToken, bool> _isCurrent;
        private readonly Action? _recordStaleCompletion;
        private readonly object _stateLock = new object();
        private long _generation;
        private long _staleCompletionCount;
        private bool _active;
        private bool _cancellationStarted;
        private int _cancellationInFlight;
        private bool _disposeRequested;
        private bool _sourceDisposed;

        public RuntimeLifetime(
            Guid runtimeId,
            Func<RuntimeGenerationToken, bool> isCurrent,
            Action? recordStaleCompletion = null)
        {
            if (runtimeId == Guid.Empty)
            {
                throw new ArgumentException("A runtime id must not be empty.", nameof(runtimeId));
            }

            RuntimeId = runtimeId;
            _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
            _recordStaleCompletion = recordStaleCompletion;
            _cancellationToken = _cancellation.Token;
        }

        public Guid RuntimeId { get; }

        public long Generation => Interlocked.Read(ref _generation);

        public CancellationToken CancellationToken => _cancellationToken;

        public long StaleCompletionCount => Interlocked.Read(ref _staleCompletionCount);

        public RuntimeGenerationToken Token => new RuntimeGenerationToken(RuntimeId, Generation);

        internal void Activate(long generation)
        {
            if (generation <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            lock (_stateLock)
            {
                if (_disposeRequested)
                {
                    throw new ObjectDisposedException(nameof(RuntimeLifetime));
                }

                if (_generation != 0)
                {
                    throw new InvalidOperationException("A runtime lifetime can only be activated once.");
                }

                Interlocked.Exchange(ref _generation, generation);
                _active = true;
            }
        }

        public bool TryAcceptCompletion()
        {
            RuntimeGenerationToken token;
            bool wasActive;
            lock (_stateLock)
            {
                wasActive = _active;
                token = new RuntimeGenerationToken(RuntimeId, _generation);
            }

            if (!wasActive)
            {
                RecordRejectedCompletion();
                return false;
            }

            var isCurrent = _isCurrent(token);
            bool accepted;
            lock (_stateLock)
            {
                accepted = _active && isCurrent;
            }

            if (!accepted)
            {
                RecordRejectedCompletion();
            }

            return accepted;
        }

        public void Retire()
        {
            bool ownsCancellation;
            lock (_stateLock)
            {
                if (!_active)
                {
                    return;
                }

                _active = false;
                ownsCancellation = BeginCancellationLocked();
            }

            if (ownsCancellation)
            {
                CancelAndComplete();
            }
        }

        public void Dispose()
        {
            bool ownsCancellation;
            bool disposeSource;
            lock (_stateLock)
            {
                if (_disposeRequested)
                {
                    return;
                }

                _disposeRequested = true;
                _active = false;
                ownsCancellation = BeginCancellationLocked();
                disposeSource = !ownsCancellation && TryClaimSourceDisposalLocked();
            }

            if (ownsCancellation)
            {
                CancelAndComplete();
            }
            else if (disposeSource)
            {
                _cancellation.Dispose();
            }
        }

        private bool BeginCancellationLocked()
        {
            if (_generation == 0 || _cancellationStarted)
            {
                return false;
            }

            _cancellationStarted = true;
            _cancellationInFlight++;
            return true;
        }

        private void CancelAndComplete()
        {
            try
            {
                _cancellation.Cancel(throwOnFirstException: false);
            }
            catch (AggregateException)
            {
                // Cancellation observers cannot invalidate an already-published replacement.
            }
            catch (ObjectDisposedException)
            {
                // State coordination prevents this; retirement remains non-throwing defensively.
            }
            finally
            {
                bool disposeSource;
                lock (_stateLock)
                {
                    _cancellationInFlight--;
                    disposeSource = TryClaimSourceDisposalLocked();
                }

                if (disposeSource)
                {
                    _cancellation.Dispose();
                }
            }
        }

        private bool TryClaimSourceDisposalLocked()
        {
            if (!_disposeRequested || _cancellationInFlight != 0 || _sourceDisposed)
            {
                return false;
            }

            _sourceDisposed = true;
            return true;
        }

        private void RecordRejectedCompletion()
        {
            Interlocked.Increment(ref _staleCompletionCount);
            _recordStaleCompletion?.Invoke();
        }
    }
}
