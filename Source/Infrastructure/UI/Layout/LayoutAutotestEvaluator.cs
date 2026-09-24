using System;
using System.Collections.Generic;
using RimMind.Presentation.UI.Layout;

namespace RimMind.Infrastructure.UI.Layout
{
    /// <summary>
    /// Evaluates the reports expected from a UI layout Autotest run.
    /// A missing report is a failed verification, not a successful skip: otherwise
    /// the game test can claim success without having rendered a requested window.
    /// </summary>
    internal static class LayoutAutotestEvaluator
    {
        public static LayoutAutotestEvaluation Evaluate(
            IEnumerable<string> requestedWindowNames,
            Func<string, LayoutReport?> getReport)
        {
            if (requestedWindowNames == null) throw new ArgumentNullException(nameof(requestedWindowNames));
            if (getReport == null) throw new ArgumentNullException(nameof(getReport));

            var details = new List<string>();
            int pass = 0;
            int fail = 0;
            int missingReports = 0;

            foreach (string windowName in requestedWindowNames)
            {
                LayoutReport? report = getReport(windowName);
                if (report == null)
                {
                    fail++;
                    missingReports++;
                    details.Add($"  [FAIL] {windowName}: no LayoutReport published before verification");
                    continue;
                }

                if (report.HasConflicts)
                {
                    fail++;
                    details.Add($"  [FAIL] {windowName}: {report.Conflicts.Count} conflict(s)");
                    foreach (LayoutConflict conflict in report.Conflicts)
                    {
                        details.Add($"    - {conflict.Message}");
                    }

                    continue;
                }

                pass++;
                details.Add($"  [PASS] {windowName}: no conflicts");
            }

            return new LayoutAutotestEvaluation(pass, fail, missingReports, details);
        }
    }

    internal sealed class LayoutAutotestEvaluation
    {
        public LayoutAutotestEvaluation(
            int passCount,
            int failCount,
            int missingReportCount,
            IReadOnlyList<string> details)
        {
            PassCount = passCount;
            FailCount = failCount;
            MissingReportCount = missingReportCount;
            Details = details ?? throw new ArgumentNullException(nameof(details));
        }

        public int PassCount { get; }
        public int FailCount { get; }
        public int MissingReportCount { get; }
        public IReadOnlyList<string> Details { get; }
        public bool IsSuccess => FailCount == 0;
    }
}
