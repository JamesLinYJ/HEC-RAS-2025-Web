using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public class EditableVectorHelpers
{
	public const int DuplicatePointPixelTolerance = 5;

	public const int DuplicateNodePixelTolerance = 10;

	public static bool IsHitTestWithinDuplicatePointTolerance(Point pt1, Point pt2, double cellSize)
	{
		return pt1.DistanceTo(pt2) < 5.0 * cellSize;
	}

	public static bool IsHitTestWithinDuplicateNodeTolerance(Point pt1, Point pt2, double cellSize)
	{
		return pt1.DistanceTo(pt2) < 10.0 * cellSize;
	}
}
