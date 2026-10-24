using Geospatial.Vectors;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public struct SplitLineResult
{
	public int FID;

	public PointOnPolyline Point;

	public CompactList<int> SnappedVertices;

	public static SplitLineResult NullResult
	{
		get
		{
			SplitLineResult result = new SplitLineResult();
			result.Point = PointOnPolyline.NullPointOnPolyline;
			return result;
		}
	}

	public bool IsSnappedToVertice => SnappedVertices.Count > 0;

	public SplitLineResult()
	{
		FID = 0;
		Point = default;
		SnappedVertices = default;
	}

	public SplitLineResult(int fid = -1)
	{
		Point = default;
		SnappedVertices = default;
		FID = fid;
	}
}
