using System;

namespace Geospatial.Vectors;

public class XSISTinFactory : DelaunayTinFactory
{
	private double _largestFlatTriangleEdge;

	public XSISTinFactory(double largestFlatTriangleEdge, TriangulationParameters tParams)
		: base(tParams)
	{
		if (double.IsNaN(largestFlatTriangleEdge))
		{
			throw new ArgumentException("largestFlatTriangleEdge");
		}
		_largestFlatTriangleEdge = largestFlatTriangleEdge;
	}

	protected override void ExecuteAfterAllInsertions()
	{
		base.ExecuteAfterAllInsertions();
		double minValueTerm = 0.0;
		int index = -1;
		while (minValueTerm != double.MinValue)
		{
			minValueTerm = double.MinValue;
			for (int i = 0; i < _tin.TriangleCount; i++)
			{
				if (IsSuperTriangle(i))
				{
					continue;
				}
				TriangleDefinition triangleDefinition = _tin.TriangleDefinition[i];
				Point4D point4D = _tin.Point4D[triangleDefinition.p0];
				Point4D point4D2 = _tin.Point4D[triangleDefinition.p1];
				Point4D point4D3 = _tin.Point4D[triangleDefinition.p2];
				if (!double.IsNaN(point4D.Z) && !double.IsNaN(point4D2.Z) && !double.IsNaN(point4D3.Z) && point4D.Z == point4D2.Z && point4D2.Z == point4D3.Z)
				{
					double maxEdgeLength = Triangle.MaxEdgeLength(point4D, point4D2, point4D3);
					if (maxEdgeLength > _largestFlatTriangleEdge && maxEdgeLength > minValueTerm)
					{
						minValueTerm = maxEdgeLength;
						index = i;
					}
				}
				if (!double.IsNaN(point4D.M) && !double.IsNaN(point4D2.M) && !double.IsNaN(point4D3.M) && point4D.M == point4D2.M && point4D2.M == point4D3.M)
				{
					double maxEdgeLength2 = Triangle.MaxEdgeLength(point4D, point4D2, point4D3);
					if (maxEdgeLength2 > _largestFlatTriangleEdge && maxEdgeLength2 > minValueTerm)
					{
						minValueTerm = maxEdgeLength2;
						index = i;
					}
				}
			}
			if (minValueTerm != double.MinValue)
			{
				Point pt = _tin.Triangles[index].Centroid();
				int t = 0;
				if (!DelaunayInsertPoint(pt, ref t, translatePoint: false, out var _))
				{
					minValueTerm = double.MinValue;
				}
			}
		}
	}
}
