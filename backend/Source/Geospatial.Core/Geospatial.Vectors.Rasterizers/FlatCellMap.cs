using System;
using System.Collections.Generic;
using System.Diagnostics;
using Utility.DataStructures;

namespace Geospatial.Vectors.Rasterizers;

[DebuggerDisplay("Cell {Index}: {PointIndexes.Count} points")]
public class FlatCellMap
{
	public int Index;

	public IList<int> PointIndexes { get; set; }

	public IList<int> MapPoints => PointIndexes;

	public FlatCellMap(int cellIndex)
	{
		Index = cellIndex;
		PointIndexes = default(CompactList<int>);
	}

	public FlatCellMap(int cellIndex, IList<int> points)
	{
		Index = cellIndex;
		PointIndexes = points;
	}

	public void SimplePaint<T>(T outputSpan2, Memory<T> output, Memory<float> clipPixelDepth)
	{
		Span<T> outputSpan = output.Span;
		if (clipPixelDepth.IsEmpty)
		{
			for (int i = 0; i < PointIndexes.Count; i++)
			{
				outputSpan[PointIndexes[i]] = outputSpan2;
			}
			return;
		}
		Span<float> clipPixelDepthSpan = clipPixelDepth.Span;
		for (int j = 0; j < PointIndexes.Count; j++)
		{
			int index = PointIndexes[j];
			if (clipPixelDepthSpan[index] > 0f)
			{
				outputSpan[index] = outputSpan2;
			}
		}
	}

	public void SimplePaint(Vector vector, VectorMeshOutputs outputs, Memory<float> clipPixelDepths)
	{
		Memory<float> outputVxes = outputs.OutputVx;
		Span<float> outputVxesSpan = outputVxes.Span;
		outputVxes = outputs.OutputVy;
		Span<float> outputVxes2 = outputVxes.Span;
		outputVxes = outputs.OutputMagnitude;
		Span<float> outputVxes3 = outputVxes.Span;
		Span<float> clipPixelDepthsSpan = clipPixelDepths.Span;
		bool isEmpty = clipPixelDepths.IsEmpty;
		float outputVxesSpan2 = (float)vector.X;
		float outputVxes22 = (float)vector.Y;
		float outputVxes32 = (float)Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
		for (int i = 0; i < MapPoints.Count; i++)
		{
			int index = MapPoints[i];
			if (isEmpty || clipPixelDepthsSpan[index] > 0f)
			{
				outputVxesSpan[index] = outputVxesSpan2;
				outputVxes2[index] = outputVxes22;
				outputVxes3[index] = outputVxes32;
			}
		}
	}
}
