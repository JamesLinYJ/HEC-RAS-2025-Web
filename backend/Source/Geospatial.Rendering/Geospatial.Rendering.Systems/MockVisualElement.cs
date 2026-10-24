using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockVisualElement : IVisualElement
{
	private readonly double _actualWidth;

	private readonly double _actualHeight;

	public double ActualWidth => _actualWidth;

	public double ActualHeight => _actualHeight;

	public MockVisualElement()
	{
	}

	public MockVisualElement(double wd, double ht)
	{
		_actualWidth = wd;
		_actualHeight = ht;
	}

	public void UpdateLayout()
	{
	}
}
