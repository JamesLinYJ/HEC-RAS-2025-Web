using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public interface ILabelDefinition
{
	string Text { get; }

	Point PointLocation { get; }

	Memory<Point> PolylineLocation { get; }
}
