using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimMind.Testing
{
    public static class ContractCaseRunner
    {
        public static void Run(params (string Name, Action Execute)[] cases)
        {
            if (cases == null)
            {
                throw new ArgumentNullException(nameof(cases));
            }

            var failures = new List<Exception>();
            foreach (var contractCase in cases)
            {
                try
                {
                    contractCase.Execute();
                }
                catch (Exception exception)
                {
                    failures.Add(CreateNamedFailure(contractCase.Name, exception));
                }
            }

            ThrowIfAnyFailed(failures, cases.Length);
        }

        public static async Task RunAsync(params (string Name, Func<Task> Execute)[] cases)
        {
            if (cases == null)
            {
                throw new ArgumentNullException(nameof(cases));
            }

            var failures = new List<Exception>();
            foreach (var contractCase in cases)
            {
                try
                {
                    await contractCase.Execute().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(CreateNamedFailure(contractCase.Name, exception));
                }
            }

            ThrowIfAnyFailed(failures, cases.Length);
        }

        private static Exception CreateNamedFailure(string name, Exception exception)
        {
            return new InvalidOperationException($"Contract scenario '{name}' failed.", exception);
        }

        private static void ThrowIfAnyFailed(IReadOnlyCollection<Exception> failures, int total)
        {
            if (failures.Count > 0)
            {
                throw new AggregateException($"{failures.Count}/{total} contract scenarios failed", failures);
            }
        }
    }
}
