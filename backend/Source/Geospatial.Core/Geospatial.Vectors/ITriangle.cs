namespace Geospatial.Vectors;

public interface ITriangle<PT> : IExtent where PT : IPoint
{
	PT PointA { get; }

	PT PointB { get; }

	PT PointC { get; }
}
