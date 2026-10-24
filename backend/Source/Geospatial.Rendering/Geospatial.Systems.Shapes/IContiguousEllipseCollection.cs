using System;
using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface IContiguousEllipseCollection : IShape, IDisposable
{
	double Width { get; set; }

	double Height { get; set; }

	Memory<Point> Points { get; }

	void DisposeInternalResources();

	void SetPoints(Memory<Point> pts);

	void ClearAndInitialize(int length);
}
