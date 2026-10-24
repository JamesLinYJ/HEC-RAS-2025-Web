using System;

namespace Geospatial.Rasters;

public sealed class NoOpDisposable : IDisposable
{
	public static readonly NoOpDisposable Instance = new NoOpDisposable();

	public void Dispose()
	{
	}
}
