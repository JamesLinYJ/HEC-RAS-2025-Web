using System;

namespace Geospatial.Core.Rasters;

public class FlatAreaDetectorDiagnostics
{
	public TimeSpan SeedTime { get; internal set; }

	public TimeSpan FillTime { get; internal set; }

	public TimeSpan MaskTime { get; internal set; }

	public TimeSpan ContourTime { get; internal set; }

	public long FillAllocatedBytes { get; internal set; }

	public long MaskAllocatedBytes { get; internal set; }

	public long ContourAllocatedBytes { get; internal set; }
}
