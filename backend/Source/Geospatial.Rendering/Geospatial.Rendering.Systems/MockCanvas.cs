using System;
using System.Collections.Generic;
using System.Drawing;
using Geospatial.Rasters.Convolution;
using Geospatial.Systems.Shapes;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockCanvas : MockVisualElement, ICanvas, IVisualElement, IDisposable
{
	private readonly List<IVisualElement> _children = new List<IVisualElement>();

	public IReadOnlyList<IVisualElement> Children => _children;

	public Color BackgroundColor { get; set; } = Color.Transparent;

	public Matrix3x3D Transformation { get; set; }

	public Matrix3x3D BaseTransform { get; set; }

	public Matrix3x3D TransformationFromBase { get; set; }

	public MockCanvas(double width, double height)
		: base(width, height)
	{
	}

	public void Dispose()
	{
	}

	public void AddChild(IVisualElement child)
	{
		_children.Add(child);
	}

	public void RemoveChild(IVisualElement child)
	{
		_children.Remove(child);
	}

	public bool HasChild(IVisualElement child)
	{
		return _children.Contains(child);
	}

	public void SetZIndex(IVisualElement element, int zIndex)
	{
	}

	public void Invalidate()
	{
	}

	public void Invalidate(double upperLeftX, double upperLeftY, double lowerRightX, double lowerRightY)
	{
	}

	public void Dispose(bool disposing)
	{
	}
}
