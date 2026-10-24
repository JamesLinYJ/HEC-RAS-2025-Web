namespace Geospatial.Vectors;

public interface ISegment<PT> where PT : IPoint
{
	PT PointA { get; set; }

	PT PointB { get; set; }
}
