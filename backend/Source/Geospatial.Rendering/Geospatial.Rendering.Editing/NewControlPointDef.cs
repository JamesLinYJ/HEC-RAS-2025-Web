using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public struct NewControlPointDef
{
	internal Point Point { get; init; }

	internal int ContainedRegionIndex { get; init; }

	public NewControlPointDef(Point point, int containedRegionIndex)
	{
		Point = point;
		ContainedRegionIndex = containedRegionIndex;
	}
}
