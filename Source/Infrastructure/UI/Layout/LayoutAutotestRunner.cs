using System;
using System.Linq;
using RimMind.Presentation.UI.Layout;
using Verse;

namespace RimMind.Infrastructure.UI.Layout
{
    /// <summary>
    /// Schedules UI layout verification after the requested windows have had an
    /// opportunity to draw, then emits the human-readable result and closes them.
    /// </summary>
    internal static class LayoutAutotestRunner
    {
        public static void Run(Window[] windows, Action<LayoutAutotestEvaluation> reportResult)
        {
            if (windows == null) throw new ArgumentNullException(nameof(windows));
            if (reportResult == null) throw new ArgumentNullException(nameof(reportResult));

            LongEventHandler.ExecuteWhenFinished(() =>
            {
                LayoutAutotestEvaluation evaluation = LayoutAutotestEvaluator.Evaluate(
                    windows.Select(window => window.GetType().Name),
                    windowName => LayoutConflictStore.TryGet(windowName, out LayoutReport? layoutReport)
                        ? layoutReport
                        : null);

                LogEvaluation(evaluation);
                reportResult(evaluation);
                CloseWindows(windows);
            });
        }

        private static void LogEvaluation(LayoutAutotestEvaluation evaluation)
        {
            string result = "[Autotests] === UI Layout Conflict Detector ===" + Environment.NewLine +
                string.Join(Environment.NewLine, evaluation.Details) + Environment.NewLine +
                $"  Result: {evaluation.PassCount} passed, {evaluation.FailCount} failed, {evaluation.MissingReportCount} missing report(s)";

            if (evaluation.IsSuccess)
            {
                Log.Message(result);
            }
            else
            {
                Log.Error(result);
            }
        }

        private static void CloseWindows(Window[] windows)
        {
            foreach (Window window in windows)
            {
                if (window.IsOpen)
                {
                    window.Close();
                }
            }
        }
    }
}
