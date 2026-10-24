using Geospatial.Systems.Shapes;

namespace Geospatial.Rendering.Systems;

public class MockEllipse : MockShape, IEllipse, IShape
{
	public double Width { get; set; }

	public double Height { get; set; }

	public float X { get; set; }

	public float Y { get; set; }

	public MockEllipse()
	{
	}

	public MockEllipse(double wd, double ht)
		: base(wd, ht)
	{
	}
}
