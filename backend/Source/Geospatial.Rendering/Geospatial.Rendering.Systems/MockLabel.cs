using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockLabel : MockVisualElement, ILabel, IVisualElement, ICanvasPositionable
{
	public double CanvasOffsetLeft { get; private set; }

	public double CanvasOffsetTop { get; private set; }

	public string Text { get; set; }

	public MockLabel()
		: base(100.0, 20.0)
	{
	}

	public void SetCanvasOffsets(double left, double top)
	{
		CanvasOffsetLeft = left;
		CanvasOffsetTop = top;
	}
}
