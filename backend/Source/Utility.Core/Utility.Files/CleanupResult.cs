using System.Collections.Generic;
using Utility.Progress;

namespace Utility.Files;

public class CleanupResult
{
	public int FilesDeleted { get; set; }

	public int FilesFailed { get; set; }

	public long BytesRecovered { get; set; }

	public List<string> FailedFiles { get; init; } = new List<string>();

	public void WriteSummary(ProgressReporter pr)
	{
		pr.ReportMessage("Cleanup complete.");
		pr.ReportMessage($"  Files deleted:   {FilesDeleted}");
		pr.ReportMessage("  Space recovered: " + FileHelpers.PrettyByteCount(BytesRecovered));
		if (FilesFailed <= 0)
		{
			return;
		}
		pr.ReportMessage($"  Files failed:    {FilesFailed}");
		foreach (string failedFile in FailedFiles)
		{
			pr.ReportMessage("    - " + failedFile);
		}
	}
}
