using System;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems.Vector;

public static class VectorManagerHelpers
{
	public static VectorType ToVectorType(this IShape shape)
	{
		if (!(shape is IEllipse))
		{
			if (!(shape is ISegment))
			{
				if (!(shape is ILine))
				{
					if (shape is IPolygon)
					{
						return VectorType.Polygon;
					}
					throw new NotImplementedException();
				}
				return VectorType.Line;
			}
			return VectorType.Segment;
		}
		return VectorType.Point;
	}
}
