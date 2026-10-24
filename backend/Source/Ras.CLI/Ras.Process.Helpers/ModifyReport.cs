using System.Collections.Generic;
using System.Text;

namespace Ras.Process.Helpers;

internal sealed class ModifyReport
{
	public string File { get; set; }

	public bool DryRun { get; set; }

	public bool Saved { get; set; }

	public int Changed { get; set; }

	public List<ModifyChange> Changes { get; set; } = new List<ModifyChange>();

	public string RenderText(string summary)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (ModifyChange change in Changes)
		{
			string operationChangeTerm = ((change.Operation != null) ? (change.Field + "." + change.Operation) : (change.Field + " = " + change.Value));
			string dryRunTerm;
			if (change.Operation == null)
			{
				dryRunTerm = (DryRun ? "would set" : "set");
			}
			else
			{
				dryRunTerm = (DryRun ? "would apply" : "applied");
			}
			StringBuilder stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler;
			if (!change.Matched.HasValue)
			{
				if (change.Changed > 0)
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder3 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(3, 3, stringBuilder2);
					handler.AppendFormatted(change.Target);
					handler.AppendLiteral(": ");
					handler.AppendFormatted(dryRunTerm);
					handler.AppendLiteral(" ");
					handler.AppendFormatted(operationChangeTerm);
					stringBuilder3.AppendLine(ref handler);
				}
				else
				{
					stringBuilder2 = stringBuilder;
					StringBuilder stringBuilder4 = stringBuilder2;
					handler = new StringBuilder.AppendInterpolatedStringHandler(19, 3, stringBuilder2);
					handler.AppendFormatted(change.Target);
					handler.AppendLiteral(": could not set ");
					handler.AppendFormatted(change.Field);
					handler.AppendLiteral(" - ");
					handler.AppendFormatted(FirstSkip(change));
					stringBuilder4.AppendLine(ref handler);
				}
				continue;
			}
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(28, 6, stringBuilder2);
			handler.AppendFormatted(change.Target);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(dryRunTerm);
			handler.AppendLiteral(" ");
			handler.AppendFormatted(operationChangeTerm);
			handler.AppendLiteral(" on ");
			handler.AppendFormatted(change.Changed);
			handler.AppendLiteral(" of ");
			handler.AppendFormatted(change.Matched);
			handler.AppendLiteral(" matched (");
			handler.AppendFormatted(change.Total);
			handler.AppendLiteral(" total)");
			stringBuilder5.AppendLine(ref handler);
			foreach (string skipped in change.Skipped)
			{
				stringBuilder.AppendLine("  skipped: " + skipped);
			}
		}
		stringBuilder.Append(summary);
		return stringBuilder.ToString();
	}

	private static string FirstSkip(ModifyChange change)
	{
		if (change.Skipped.Count <= 0)
		{
			return "unknown reason";
		}
		return change.Skipped[0];
	}
}
