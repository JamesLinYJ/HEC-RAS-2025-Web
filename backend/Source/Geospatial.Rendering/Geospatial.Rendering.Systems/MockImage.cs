using System.Numerics;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockImage : MockVisualElement, IImage, IVisualElement, ICanvasPositionable
{
	public double CanvasOffsetLeft { get; private set; }

	public double CanvasOffsetTop { get; private set; }

	public IBitmap Source { get; set; }

	public Vector3 Scale { get; set; }

	public Vector3 CenterPoint { get; set; }

	public MockImage(double width, double height)
		: base(width, height)
	{
	}

	public void SetCanvasOffsets(double left, double top)
	{
		CanvasOffsetLeft = left;
		CanvasOffsetTop = top;
	}

	public void ResetScale()
	{
	}
}
