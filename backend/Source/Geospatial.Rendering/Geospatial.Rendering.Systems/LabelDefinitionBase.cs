using System;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public abstract class LabelDefinitionBase : ILabelDefinition
{
	public abstract string Text { get; }

	public Point PointLocation { get; set; }

	public float PointLocationRotation { get; set; }

	public Memory<Point> PolylineLocation { get; set; }
}
