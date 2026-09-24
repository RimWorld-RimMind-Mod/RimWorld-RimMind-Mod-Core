using System;
using System.Threading;

namespace RimMind.Application.Features.Requests
{
    internal sealed class RequestCancellationRegistrations : IDisposable
    {
        private readonly IDisposable _runtimeRegistration;
        private readonly IDisposable _callerRegistration;
        private int _disposed;

        private RequestCancellationRegistrations(
            IDisposable runtimeRegistration,
            IDisposable callerRegistration)
        {
            _runtimeRegistration = runtimeRegistration;
            _callerRegistration = callerRegistration;
        }

        public static bool TryCreate(
            CancellationToken runtimeToken,
            CancellationToken callerToken,
            Action cancellationCallback,
            out RequestCancellationRegistrations? registrations,
            out Exception? setupFailure)
            => TryCreate(
                callback => runtimeToken.Register(callback),
                callback => callerToken.Register(callback),
                cancellationCallback,
                out registrations,
                out setupFailure);

        internal static bool TryCreate(
            Func<Action, IDisposable> registerRuntime,
            Func<Action, IDisposable> registerCaller,
            Action cancellationCallback,
            out RequestCancellationRegistrations? registrations,
            out Exception? setupFailure)
        {
            IDisposable? runtimeRegistration = default;
            IDisposable? callerRegistration = default;
            try
            {
                runtimeRegistration = registerRuntime(cancellationCallback);
                callerRegistration = registerCaller(cancellationCallback);
                registrations = new RequestCancellationRegistrations(
                    runtimeRegistration,
                    callerRegistration);
                setupFailure = null;
                return true;
            }
            catch (Exception ex)
            {
                callerRegistration?.Dispose();
                runtimeRegistration?.Dispose();
                registrations = null;
                setupFailure = ex;
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _callerRegistration.Dispose();
            _runtimeRegistration.Dispose();
        }
    }
}
