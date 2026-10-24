namespace Geospatial.Vectors;

public struct ConstrainedEdge(int pA, int pB, int tri, bool isPerim, int enforceIdx)
{
	public int PointA = pA;

	public int PointB = pB;

	public int Triangle = tri;

	public bool IsPerimeter = isPerim;

	public int EnforcedFromIndex = enforceIdx;

	public (int, int) SortedPointPair()
	{
		if (PointA <= PointB)
		{
			return (PointA, PointB);
		}
		return (PointB, PointA);
	}
}
