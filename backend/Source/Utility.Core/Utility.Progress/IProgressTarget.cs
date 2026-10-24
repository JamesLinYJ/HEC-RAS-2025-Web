using Utility.Logging;

namespace Utility.Progress;

public interface IProgressTarget
{
	void WriteMessage(ProgressReporter sender, Message message);

	void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress);
}
