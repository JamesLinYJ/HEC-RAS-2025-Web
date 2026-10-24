using System;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockContiguousEllipseCollection : MockShape, IContiguousEllipseCollection, IShape, IDisposable
{
	public double Width { get; set; }

	public double Height { get; set; }

	public Memory<Point> Points { get; private set; }

	public void DisposeInternalResources()
	{
	}

	public void ClearAndInitialize(int length)
	{
		Points = new Point[length];
	}

	public void Dispose()
	{
	}

	public void SetPoints(Memory<Point> pts)
	{
		Points = pts;
	}
}
