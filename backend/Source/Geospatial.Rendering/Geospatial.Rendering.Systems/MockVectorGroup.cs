using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;
using Utility.VisualElements;

namespace Geospatial.Rendering.Systems;

public class MockVectorGroup : IVectorCanvas, IVisualElement, IDisposable
{
	private List<IShape> _shapes = new List<IShape>();

	private double _width;

	private double _height;

	public Matrix4x4 Transformation { get; set; }

	public double StrokeThickness { get; set; }

	public Color Fill { get; set; }

	public Color Stroke { get; set; }

	public IReadOnlyList<IShape> Shapes => _shapes;

	public int Count => _shapes.Count;

	public double ActualWidth => _width;

	public double ActualHeight => _height;

	public bool IsStrokeDotted { get; set; }

	public float[] DottedStrokeStyle { get; set; }

	public bool IsTranslated { get; set; }

	public float[] Translation { get; set; }

	public Color HaloColor { get; set; }

	public float HaloThickness { get; set; }

	public string Font { get; set; }

	public int TextEmphasis { get; set; }

	public int TextMaxWidth { get; set; }

	public float TextKerning { get; set; }

	public Memory<Geospatial.Vectors.Point> ClipExtent { get; set; }

	public MockVectorGroup()
	{
	}

	public MockVectorGroup(double width, double height)
	{
		_width = width;
		_height = height;
	}

	public void AddShape(IShape child)
	{
		_shapes.Add(child);
	}

	public bool ContainsShape(IShape child)
	{
		return _shapes.Contains(child);
	}

	public void RemoveShape(IShape child)
	{
		_shapes.Remove(child);
	}

	public void TrimShapesFromEnd(int count)
	{
		_shapes.RemoveRange(_shapes.Count - count, count);
	}

	public void UpdateLayout()
	{
	}

	public void Clear()
	{
		_shapes.Clear();
	}

	public void DisposeInternalResources()
	{
	}

	public void Dispose()
	{
	}

	public void CompleteClip()
	{
	}
}
