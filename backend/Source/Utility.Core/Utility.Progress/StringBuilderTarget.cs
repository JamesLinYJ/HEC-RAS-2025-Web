using System.Text;
using Utility.Logging;

namespace Utility.Progress;

public class StringBuilderTarget : IProgressTarget
{
	public StringBuilder StringBuilder { get; }

	public StringBuilderTarget()
	{
		StringBuilder = new StringBuilder();
	}

	public StringBuilderTarget(StringBuilder sb)
	{
		StringBuilder = sb;
	}

	public void WriteMessage(ProgressReporter sender, Message message)
	{
		StringBuilder.AppendLine(message.EvaluatedMessage);
	}

	public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
	{
	}
}
