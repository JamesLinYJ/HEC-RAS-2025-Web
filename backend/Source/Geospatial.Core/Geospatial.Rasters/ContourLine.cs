using Geospatial.Vectors;

namespace Geospatial.Rasters;

public readonly struct ContourLine(Polyline line, float level, bool isMajor)
{
	public readonly Polyline Line = line;

	public readonly float Level = level;

	public readonly bool IsMajor = isMajor;
}
