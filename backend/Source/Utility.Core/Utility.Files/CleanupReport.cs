using System.Collections.Generic;
using System.Linq;
using Utility.Progress;

namespace Utility.Files;

public class CleanupReport
{
	public List<CleanupCandidate> Candidates { get; init; } = new List<CleanupCandidate>();

	public int TotalFilesScanned { get; set; }

	public long TotalStorageBytes { get; set; }

	public int NamedBackupsSkipped { get; set; }

	public List<CleanupEvaluation> Evaluations { get; init; } = new List<CleanupEvaluation>();

	public long RecoverableBytes => Candidates.Sum((CleanupCandidate c) => c.TotalRecoverableBytes);

	public long RetainedBytes => TotalStorageBytes - RecoverableBytes;

	public List<CleanupCandidate> GetCandidates(string category)
	{
		return Candidates.Where((CleanupCandidate c) => c.Category == category).ToList();
	}

	public void WriteSummary(ProgressReporter pr)
	{
		pr.ReportMessage("Cleanup scan complete.");
		pr.ReportMessage($"  Files scanned:     {TotalFilesScanned}");
		pr.ReportMessage("  Total storage:     " + FileHelpers.PrettyByteCount(TotalStorageBytes));
		pr.ReportMessage($"  Named (skipped):   {NamedBackupsSkipped}");
		pr.ReportMessage($"  Candidates:        {Candidates.Count}");
		pr.ReportMessage("  Recoverable:       " + FileHelpers.PrettyByteCount(RecoverableBytes));
		pr.ReportMessage("  Retained:          " + FileHelpers.PrettyByteCount(RetainedBytes));
		foreach (IGrouping<string, CleanupCandidate> iGrouping in from c in Candidates
			group c by c.Category)
		{
			long numBytes = iGrouping.Sum((CleanupCandidate c) => c.TotalRecoverableBytes);
			pr.ReportMessage($"  {iGrouping.Key}: {iGrouping.Count()} ({FileHelpers.PrettyByteCount(numBytes)})");
		}
	}
}
