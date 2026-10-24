using System;
using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Systems.Shapes;

public interface IContiguousLineCollection : IDisposable
{
	Memory<Point> Lines { get; }

	Memory<int> LineStartIndices { get; }

	int PointCount { get; }

	int LineCount { get; }

	void DisposeInternalResources();

	void SetLines(IReadOnlyList<Memory<Point>> lines);

	void SetLines<T>(IReadOnlyList<T> lines) where T : IReadOnlyList<Point>;

	Memory<Point> GetLine(int index);
}
