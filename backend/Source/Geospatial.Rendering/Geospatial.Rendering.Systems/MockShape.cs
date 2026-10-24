using System.Drawing;
using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockShape : MockVisualElement, IShape
{
	public double StrokeThickness { get; set; }

	public Color Fill { get; set; }

	public Color Stroke { get; set; }

	public object ShapeElement { get; set; }

	public MockShape()
	{
	}

	public MockShape(double wd, double ht)
		: base(wd, ht)
	{
	}
}
