using System;

namespace Utility.Memory;

public interface IPausablePrefetch
{
	IDisposable PauseWarming();
}
