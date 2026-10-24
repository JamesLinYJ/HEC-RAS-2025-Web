using System.Diagnostics;

namespace Geospatial.Vectors;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public struct LineCollectionIntersection
{
	public static readonly LineCollectionIntersection NullIntersection = new LineCollectionIntersection(LineIntersection.NullIntersection, -1, -1);

	public readonly int APolylineIndex;

	public readonly int BPolylineIndex;

	public readonly LineIntersection Intersection;

	public bool IsNull => Intersection.IsNull;

	public LineCollectionIntersection(LineIntersection intersection, int aPolylineIndex, int bPolylineIndex)
	{
		Intersection = intersection;
		APolylineIndex = aPolylineIndex;
		BPolylineIndex = bPolylineIndex;
	}

	public LineCollectionIntersection SwapAB()
	{
		return new LineCollectionIntersection(Intersection.SwapAB(), BPolylineIndex, APolylineIndex);
	}

	private string GetDebuggerDisplay()
	{
		return "(" + APolylineIndex + " x " + BPolylineIndex + ") " + Intersection.Point.ToString();
	}
}
