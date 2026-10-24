using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems.Vector;

public static class VectorTypeHelpers
{
	public static VectorType GetBestVectorType(object localValue)
	{
		if (!(localValue is Polygon))
		{
			if (!(localValue is Polyline))
			{
				if (!(localValue is Point))
				{
					if (!(localValue is Segment))
					{
						if (!(localValue is ComplexPolygon))
						{
							if (localValue is string)
							{
								return VectorType.Text;
							}
							throw new NotImplementedException();
						}
						return VectorType.ComplexPolygon;
					}
					return VectorType.Segment;
				}
				return VectorType.Point;
			}
			return VectorType.Line;
		}
		return VectorType.Polygon;
	}
}
