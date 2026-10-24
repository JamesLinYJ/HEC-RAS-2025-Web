using System;

namespace Utility.Logging;

internal sealed class NullScope : IDisposable
{
	public static readonly NullScope Instance = new NullScope();

	private NullScope()
	{
	}

	public void Dispose()
	{
	}
}
