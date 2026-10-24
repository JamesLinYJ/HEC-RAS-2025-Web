using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Progress;

public class LoggerProgressTarget : IProgressTarget
{
	private readonly ILogger _target;

	public LoggerProgressTarget(ILogger target)
	{
		_target = target;
	}

	public void WriteMessage(ProgressReporter sender, Message message)
	{
		if (message.MessageType.DefaultSeverity == MessageSeverity.Error)
		{
			_target.LogError(message.EvaluatedMessage);
		}
		else if (message.MessageType.DefaultSeverity == MessageSeverity.Warning)
		{
			_target.LogWarning("WARNING: {msg}", message);
		}
		else
		{
			_target.LogTrace(message.EvaluatedMessage);
		}
	}

	public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
	{
		_target.LogTrace("PROGRESS={prog}%", globalProgress);
	}
}
