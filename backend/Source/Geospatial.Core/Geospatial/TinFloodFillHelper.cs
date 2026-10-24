using System;
using Geospatial.Vectors;

namespace Geospatial;

public class TinFloodFillHelper : FloodFillHelper
{
	public TinFloodFillHelper(Tin tin, Func<int, int, bool> fromToIncludeTest)
		: base(GetTinNeighbors(tin), fromToIncludeTest)
	{
	}

	public static TinFloodFillHelper CheckConstraints(DelaunayTinFactory tf)
	{
		Tin tin = tf.Tin;
		return new TinFloodFillHelper(tin, (int tFrom, int tTo) =>
		{
			if (tTo == -1)
			{
				return false;
			}
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[tFrom];
			_ = tin.TriangleDefinition[tTo];
			int edge = triangleDefinition.GetEdge012(tTo);
			triangleDefinition.EdgePoints(edge, out var ptA, out var ptB);
			bool isPerimeter;
			int constraintIdx;
			return !tf.IsConstrained(ptA, ptB, out isPerimeter, out constraintIdx);
		});
	}

	private static Func<int, Memory<int>> GetTinNeighbors(Tin tin)
	{
		int[] triAts = new int[3];
		return (int tIdx) =>
		{
			TriangleDefinition triangleDefinition = tin.TriangleDefinition[tIdx];
			triAts[0] = triangleDefinition.at0;
			triAts[1] = triangleDefinition.at1;
			triAts[2] = triangleDefinition.at2;
			return triAts;
		};
	}
}
