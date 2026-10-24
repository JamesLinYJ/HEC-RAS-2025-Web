using System;
using Utility.Logging;

namespace Utility.Progress;

public sealed class CompositeProgressTarget : IProgressTarget
{
	private readonly IProgressTarget[] _targets;

	public CompositeProgressTarget(params IProgressTarget[] targets)
	{
		_targets = targets ?? throw new ArgumentNullException("targets");
	}

	public void WriteMessage(ProgressReporter sender, Message message)
	{
		for (int i = 0; i < _targets.Length; i++)
		{
			_targets[i]?.WriteMessage(sender, message);
		}
	}

	public void WriteProgress(ProgressReporter sender, int localProgress, int globalProgress)
	{
		for (int i = 0; i < _targets.Length; i++)
		{
			_targets[i]?.WriteProgress(sender, localProgress, globalProgress);
		}
	}
}
