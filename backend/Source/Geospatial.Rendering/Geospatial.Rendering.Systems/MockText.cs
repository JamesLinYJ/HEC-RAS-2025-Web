using System;
using Geospatial.Rasters.Convolution;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockText : MockShape, IText, IShape, IDisposable
{
	private Polyline _line = new Polyline();

	public string Text { get; set; }

	public float X { get; set; }

	public float Y { get; set; }

	public float RotationDegrees { get; set; }

	public Polyline LineAlignment => _line;

	public ILabelDefinition Label { get; set; }

	public Matrix3x3D DataTransform { get; set; }

	public void SetLineAlignment(Memory<Point> lineAlignment)
	{
		_line = new Polyline(lineAlignment);
	}

	public void Dispose()
	{
	}
}
