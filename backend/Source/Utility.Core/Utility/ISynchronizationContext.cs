using System;

namespace Utility;

public interface ISynchronizationContext
{
	void Sync(Action action);

	void Sync();
}
