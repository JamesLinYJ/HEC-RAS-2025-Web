using System;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility;

public interface ISynchronizeable
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ISynchronizeable");

	ISynchronizationContext SynchronizationContext { get; set; }

	void Synchronize(Action action)
	{
		if (SynchronizationContext == null)
		{
			_logger.LogError("FAILED to synchronize because of null SynchronizationContext, this is properly due to misordered binding evaluation.");
		}
		else
		{
			SynchronizationContext.Sync(action);
		}
	}
}
