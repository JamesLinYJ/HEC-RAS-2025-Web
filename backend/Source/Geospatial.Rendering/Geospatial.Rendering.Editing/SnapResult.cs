using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public struct SnapResult
{
	public SnapMode Result;

	public Point SnapPoint;

	public CompactList<SplitLineResult> SplitLines;

	public static SnapResult NoSnap => new SnapResult(SnapMode.NoSnap);

	public bool Success
	{
		get
		{
			if (Result != SnapMode.NoSnap)
			{
				return Result != SnapMode.FailedLineIntersection;
			}
			return false;
		}
	}

	public SnapResult()
	{
		SnapPoint = default;
		SplitLines = default;
		Result = SnapMode.NoSnap;
	}

	public SnapResult(SnapMode result, Point snapPoint = default(Point))
	{
		SplitLines = default;
		Result = result;
		SnapPoint = snapPoint;
	}

	public SnapResult(SnapMode result, SplitLineResult splitLineResult)
	{
		Result = result;
		SplitLines = new CompactList<SplitLineResult>(splitLineResult);
		SnapPoint = splitLineResult.Point.Point;
	}
}
