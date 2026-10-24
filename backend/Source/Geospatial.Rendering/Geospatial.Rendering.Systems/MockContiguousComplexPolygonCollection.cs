using System;
using System.Collections.Generic;
using Geospatial.Systems.Shapes;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Systems;

public class MockContiguousComplexPolygonCollection : MockShape, IContiguousComplexPolygonCollection, IDisposable
{
	private Point[] _points;

	private int[] _polygonStartIndices;

	public Memory<Point> Points { get; private set; }

	public Memory<int> PolygonStartIndices { get; private set; }

	public int PointCount => Points.Length;

	public void DisposeInternalResources()
	{
		_points = null;
		_polygonStartIndices = null;
		Points = default;
		PolygonStartIndices = default;
	}

	public void Dispose()
	{
	}

	public Memory<Point> GetPolygon(int polygonIndex)
	{
		if (polygonIndex < 0 || polygonIndex >= PolygonStartIndices.Length)
		{
			return Memory<Point>.Empty;
		}
		int polygonStartIndex = _polygonStartIndices[polygonIndex];
		int polygonIndexPolygonStartIndicesTerm = ((polygonIndex + 1 == PolygonStartIndices.Length) ? PointCount : _polygonStartIndices[polygonIndex + 1]) - polygonStartIndex;
		if (polygonIndexPolygonStartIndicesTerm <= 0)
		{
			return Memory<Point>.Empty;
		}
		return Points.Slice(polygonStartIndex, polygonIndexPolygonStartIndicesTerm);
	}

	public void SetComplexPolygons(IReadOnlyList<ImmutableMultiPartPolygon> complexPolygons)
	{
		DisposeInternalResources();
		if (complexPolygons == null || complexPolygons.Count == 0)
		{
			return;
		}
		int pointLengthSum = 0;
		int polygonCountSum = 0;
		for (int i = 0; i < complexPolygons.Count; i++)
		{
			pointLengthSum += complexPolygons[i].Points.Length;
			polygonCountSum += complexPolygons[i].PolygonCount;
		}
		_points = new Point[pointLengthSum];
		_polygonStartIndices = new int[polygonCountSum];
		Points = _points.AsMemory();
		PolygonStartIndices = _polygonStartIndices.AsMemory();
		int ordinal = 0;
		int polygonLengthSum = 0;
		for (int j = 0; j < complexPolygons.Count; j++)
		{
			ImmutableMultiPartPolygon immutableMultiPartPolygon = complexPolygons[j];
			for (int k = 0; k < immutableMultiPartPolygon.PolygonCount; k++)
			{
				_polygonStartIndices[ordinal] = polygonLengthSum;
				Memory<Point> polygon = immutableMultiPartPolygon.GetPolygon(k);
				polygon.CopyTo(Points.Slice(polygonLengthSum, polygon.Length));
				polygonLengthSum += polygon.Length;
				ordinal++;
			}
		}
	}

	public void SetComplexPolygons(IReadOnlyList<ComplexPolygon> complexPolygons)
	{
		DisposeInternalResources();
		if (complexPolygons == null || complexPolygons.Count == 0)
		{
			return;
		}
		int ringCountSum = 0;
		int ringCountSum2 = 0;
		for (int i = 0; i < complexPolygons.Count; i++)
		{
			ComplexPolygon complexPolygon = complexPolygons[i];
			for (int j = 0; j < complexPolygon.RingCount; j++)
			{
				ringCountSum += complexPolygon.Rings[j].Count;
			}
			ringCountSum2 += complexPolygon.RingCount;
		}
		_points = new Point[ringCountSum];
		_polygonStartIndices = new int[ringCountSum2];
		Points = _points.AsMemory();
		PolygonStartIndices = _polygonStartIndices.AsMemory();
		int ordinal = 0;
		int polygonCountSum = 0;
		for (int k = 0; k < complexPolygons.Count; k++)
		{
			ComplexPolygon complexPolygon2 = complexPolygons[k];
			for (int l = 0; l < complexPolygon2.RingCount; l++)
			{
				_polygonStartIndices[ordinal] = polygonCountSum;
				Polygon polygon = complexPolygon2.Rings[l];
				polygon.GetPointBufferUnsafe().CopyTo(Points.Slice(polygonCountSum, polygon.Count));
				polygonCountSum += polygon.Count;
				ordinal++;
			}
		}
	}
}
