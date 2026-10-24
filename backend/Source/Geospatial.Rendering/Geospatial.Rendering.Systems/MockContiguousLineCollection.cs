using System;
using System.Collections.Generic;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockContiguousLineCollection : MockShape, IContiguousLineCollection, IDisposable
{
	private Point[] _points;

	private int[] _lineStartIndices;

	public Memory<Point> Lines { get; private set; }

	public Memory<int> LineStartIndices { get; private set; }

	public int PointCount => Lines.Length;

	public int LineCount => LineStartIndices.Length;

	public void Dispose()
	{
	}

	public void DisposeInternalResources()
	{
		_points = null;
		_lineStartIndices = null;
		Lines = default;
		LineStartIndices = default;
	}

	public Memory<Point> GetLine(int index)
	{
		if (index < 0 || index >= LineCount)
		{
			return Memory<Point>.Empty;
		}
		int lineStartIndex = _lineStartIndices[index];
		int indexLineCountTerm = ((index + 1 == LineCount) ? PointCount : _lineStartIndices[index + 1]) - lineStartIndex;
		if (indexLineCountTerm <= 0)
		{
			return Memory<Point>.Empty;
		}
		return Lines.Slice(lineStartIndex, indexLineCountTerm);
	}

	public void SetLines(IReadOnlyList<Memory<Point>> lines)
	{
		DisposeInternalResources();
		if (lines != null && lines.Count != 0)
		{
			int lineLengthSum = 0;
			for (int i = 0; i < lines.Count; i++)
			{
				lineLengthSum += lines[i].Length;
			}
			_points = new Point[lineLengthSum];
			_lineStartIndices = new int[lines.Count];
			Lines = _points.AsMemory();
			LineStartIndices = _lineStartIndices.AsMemory();
			int lineLengthSum2 = 0;
			for (int j = 0; j < lines.Count; j++)
			{
				_lineStartIndices[j] = lineLengthSum2;
				lines[j].CopyTo(Lines.Slice(lineLengthSum2, lines[j].Length));
				lineLengthSum2 += lines[j].Length;
			}
		}
	}

	public void SetLines<T>(IReadOnlyList<T> lines) where T : IReadOnlyList<Point>
	{
		DisposeInternalResources();
		if (lines == null || lines.Count == 0)
		{
			return;
		}
		int lineCountSum = 0;
		for (int i = 0; i < lines.Count; i++)
		{
			lineCountSum += lines[i].Count;
		}
		_points = new Point[lineCountSum];
		_lineStartIndices = new int[lines.Count];
		Lines = _points.AsMemory();
		LineStartIndices = _lineStartIndices.AsMemory();
		int lineCountSum2 = 0;
		for (int j = 0; j < lines.Count; j++)
		{
			_lineStartIndices[j] = lineCountSum2;
			Memory<Point> linesSpan = Lines;
			linesSpan = linesSpan.Slice(lineCountSum2, lines[j].Count);
			Span<Point> lines2 = linesSpan.Span;
			for (int k = 0; k < lines[j].Count; k++)
			{
				lines2[k] = lines[j][k];
			}
			lineCountSum2 += lines[j].Count;
		}
	}
}
