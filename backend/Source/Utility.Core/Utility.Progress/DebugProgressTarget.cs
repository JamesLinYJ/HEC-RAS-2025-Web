using Utility.Logging;

namespace Utility.Progress;

public class DebugProgressTarget : IProgressTarget
{
	public void WriteMessage(ProgressReporter sender, Message message)
	{
	}

	public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
	{
	}
}
