using System;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Rasters;

namespace Geospatial.Vectors.Rasterizers;

public class PolygonRasterizer
{
	private RasterDefinition _raster;

	private List<double>[] _rowData;

	private int _absMinRow;

	private int _absMaxRow;

	private static readonly ThreadLocal<HashSet<int>> _rentAHash = new ThreadLocal<HashSet<int>>(() => new HashSet<int>());

	public static PolygonRasterizer MakeUninitializedPolygonRasterizer()
	{
		return new PolygonRasterizer();
	}

	private PolygonRasterizer()
	{
		SetupArrays(0);
	}

	public PolygonRasterizer(RasterDefinition raster)
	{
		if (raster.IsNull())
		{
			throw new ArgumentNullException("raster");
		}
		_raster = raster;
		SetupArrays(raster.Rows);
	}

	public void Reset(RasterDefinition raster)
	{
		_raster = raster;
		for (int i = 0; i < _rowData.Length; i++)
		{
			_rowData[i].Clear();
		}
		if (_rowData.Length == raster.Rows)
		{
			return;
		}
		if (_rowData.Length < raster.Rows)
		{
			List<double>[] rowData = new List<double>[raster.Rows];
			_rowData.CopyTo(rowData.AsMemory());
			int rowDataLength = _rowData.Length;
			_rowData = rowData;
			for (int j = rowDataLength; j < raster.Rows; j++)
			{
				_rowData[j] = new List<double>();
			}
		}
		else
		{
			List<double>[] rowDataList = new List<double>[raster.Rows];
			_rowData.AsMemory().Slice(0, raster.Rows).CopyTo(rowDataList.AsMemory());
			_rowData = rowDataList;
		}
	}

	private void SetupArrays(int rows)
	{
		_rowData = new List<double>[rows];
		for (int i = 0; i < rows; i++)
		{
			_rowData[i] = new List<double>();
		}
	}

	public void Rasterize<TPolygon>(TPolygon polygon, Action<int> fillCellAction) where TPolygon : ISimpleRing
	{
		Rasterize(polygon, (int row, int startC, int stopC) =>
		{
			int rowTimesCols = row * _raster.Cols;
			for (int i = startC; i <= stopC; i++)
			{
				fillCellAction(rowTimesCols + i);
			}
		});
	}

	public bool Rasterize<TPolygon, TPayload>(TPolygon polygon, TPayload t, Action<int, TPayload> fillCellAction) where TPolygon : ISimpleRing
	{
		(Action<int, TPayload>, TPayload, int) t2 = (fillCellAction, t, _raster.Cols);
		return Rasterize(polygon, t2, (int row, int startC, int stopC, (Action<int, TPayload>, TPayload, int) pl) =>
		{
			int plTerm = pl.Item3;
			Action<int, TPayload> plTerm2 = pl.Item1;
			TPayload tPayload = pl.Item2;
			int rowTerm = row * plTerm;
			for (int i = startC; i <= stopC; i++)
			{
				plTerm2(rowTerm + i, tPayload);
			}
		});
	}

	public void Rasterize(ComplexPolygon polygon, Action<int> fillCellAction)
	{
		Rasterize(polygon, (int row, int startC, int stopC) =>
		{
			int rowTimesCols = row * _raster.Cols;
			for (int i = startC; i <= stopC; i++)
			{
				fillCellAction(rowTimesCols + i);
			}
		});
	}

	public bool Rasterize<T>(ComplexPolygon polygon, T payload, Action<int, T> fillCellAction)
	{
		(Action<int, T>, T, int) payload2 = (fillCellAction, payload, _raster.Cols);
		return Rasterize(polygon, payload2, (int row, int startC, int stopC, (Action<int, T>, T, int) tuple) =>
		{
			int rowTupleTerm = row * tuple.Item3;
			for (int i = startC; i <= stopC; i++)
			{
				tuple.Item1(rowTupleTerm + i, tuple.Item2);
			}
		});
	}

	public void RasterizePartialPixel<TPolygon>(TPolygon polygon, Action<int> fillCellAction) where TPolygon : ISimpleRing
	{
		HashSet<int> hs = _rentAHash.Value;
		hs.Clear();
		Rasterize(polygon, (int i) =>
		{
			hs.Add(i);
			fillCellAction(i);
		});
		for (int ordinal = 0; ordinal < polygon.SegmentCount; ordinal++)
		{
			polygon.GetSegment(ordinal).Rasterize(_raster, (int i) =>
			{
				if (hs.Add(i))
				{
					fillCellAction(i);
				}
			});
		}
	}

	public bool RasterizePartialPixel<TPolygon, TPayload>(TPolygon polygon, TPayload t, Action<int, TPayload> fillCellAction) where TPolygon : ISimpleRing
	{
		bool condition = false;
		HashSet<int> rentAHashTerm = _rentAHash.Value;
		rentAHashTerm.Clear();
		(Action<int, TPayload>, TPayload, HashSet<int>) t2 = (fillCellAction, t, rentAHashTerm);
		condition |= Rasterize(polygon, t2, (int i, (Action<int, TPayload>, TPayload, HashSet<int>) payload) =>
		{
			payload.Item3.Add(i);
			payload.Item1(i, payload.Item2);
		});
		for (int ordinal = 0; ordinal < polygon.SegmentCount; ordinal++)
		{
			condition |= polygon.GetSegment(ordinal).Rasterize(_raster, t2, (int i, (Action<int, TPayload>, TPayload, HashSet<int>) payload) =>
			{
				if (payload.Item3.Add(i))
				{
					payload.Item1(i, payload.Item2);
				}
			});
		}
		return condition;
	}

	public void RasterizeCompletePixel(Polygon polygon, Action<int, bool> fillOrUnfillCellAction)
	{
		HashSet<int> hs = new HashSet<int>();
		Rasterize(polygon, (int i) =>
		{
			fillOrUnfillCellAction(i, arg2: true);
		});
		foreach (Segment segment in polygon.Segments)
		{
			segment.Rasterize(_raster, (int i) =>
			{
				if (hs.Add(i))
				{
					fillOrUnfillCellAction(i, arg2: false);
				}
			});
		}
	}

	public bool RasterizeCompletePixel<T>(Polygon polygon, T t, Action<int, bool, T> fillOrUnfillCellAction)
	{
		bool condition = false;
		HashSet<int> localValue = new HashSet<int>();
		condition |= Rasterize(polygon, (fillOrUnfillCellAction, t), (int i, (Action<int, bool, T>, T) payload) =>
		{
			payload.Item1(i, arg2: true, payload.Item2);
		});
		(Action<int, bool, T>, T, HashSet<int>) t2 = (fillOrUnfillCellAction, t, localValue);
		foreach (Segment segment in polygon.Segments)
		{
			condition |= segment.Rasterize(_raster, t2, (int i, (Action<int, bool, T>, T, HashSet<int>) payload) =>
			{
				if (payload.Item3.Add(i))
				{
					payload.Item1(i, arg2: false, payload.Item2);
				}
			});
		}
		return condition;
	}

	public void RasterizePartialPixel(ComplexPolygon polygon, Action<int> fillCellAction)
	{
		HashSet<int> hs = new HashSet<int>();
		Rasterize(polygon, (int i) =>
		{
			hs.Add(i);
			fillCellAction(i);
		});
		foreach (Segment segment in polygon.Segments)
		{
			segment.Rasterize(_raster, (int i) =>
			{
				if (hs.Add(i))
				{
					fillCellAction(i);
				}
			});
		}
	}

	public void RasterizePartialPixel<T>(ComplexPolygon polygon, T payload, Action<int, T> fillCellAction)
	{
		HashSet<int> localValue = new HashSet<int>();
		(Action<int, T>, T, HashSet<int>) tuple = (fillCellAction, payload, localValue);
		Rasterize(polygon, tuple, (int i, (Action<int, T>, T, HashSet<int>) tuple2) =>
		{
			tuple2.Item3.Add(i);
			tuple2.Item1(i, tuple2.Item2);
		});
		foreach (Segment segment in polygon.Segments)
		{
			segment.Rasterize(_raster, tuple, (int i, (Action<int, T>, T, HashSet<int>) tuple2) =>
			{
				if (tuple2.Item3.Add(i))
				{
					tuple2.Item1(i, tuple2.Item2);
				}
			});
		}
	}

	public void RasterizeCompletePixel(ComplexPolygon polygon, Action<int, bool> fillOrUnfillCellAction)
	{
		HashSet<int> hs = new HashSet<int>();
		Rasterize(polygon, (int i) =>
		{
			hs.Add(i);
			fillOrUnfillCellAction(i, arg2: true);
		});
		foreach (Segment segment in polygon.Segments)
		{
			segment.Rasterize(_raster, (int i) =>
			{
				if (hs.Add(i))
				{
					fillOrUnfillCellAction(i, arg2: false);
				}
			});
		}
	}

	public void Rasterize<TPolygon>(TPolygon polygon, Action<int, int, int> rowStartStopColAction) where TPolygon : ISimpleRing
	{
		Extent extent = polygon.Extent;
		Initialize(extent);
		BuildSegmentIntersectionList(polygon);
		SortIntersections();
		Replay(rowStartStopColAction);
	}

	public bool Rasterize<TPolygon, TPayload>(TPolygon polygon, TPayload t, Action<int, int, int, TPayload> rowStartStopColAction) where TPolygon : ISimpleRing
	{
		Extent extent = polygon.Extent;
		Initialize(extent);
		BuildSegmentIntersectionList(polygon);
		SortIntersections();
		return Replay(t, rowStartStopColAction);
	}

	public void Rasterize(ComplexPolygon polygon, Action<int, int, int> rowStartStopColAction)
	{
		Extent extent = polygon.Extent;
		Initialize(extent);
		foreach (Polygon ring in polygon.Rings)
		{
			BuildSegmentIntersectionList(ring);
		}
		SortIntersections();
		Replay(rowStartStopColAction);
	}

	public bool Rasterize<T>(ComplexPolygon polygon, T payload, Action<int, int, int, T> rowStartStopColAction)
	{
		Extent extent = polygon.Extent;
		Initialize(extent);
		foreach (Polygon ring in polygon.Rings)
		{
			BuildSegmentIntersectionList(ring);
		}
		SortIntersections();
		return Replay(payload, rowStartStopColAction);
	}

	private void Initialize(Extent ext)
	{
		_absMinRow = _raster.Row(Math.Min(ext.MaxY, _raster.MaxY));
		_absMaxRow = _raster.Row(Math.Max(ext.MinY, _raster.MinY));
		if (_absMinRow < 0)
		{
			_absMinRow = 0;
		}
		if (_absMaxRow >= _raster.Rows)
		{
			_absMaxRow = _raster.Rows - 1;
		}
		for (int i = _absMinRow; i <= _absMaxRow; i++)
		{
			_rowData[i].Clear();
		}
	}

	private void BuildSegmentIntersectionList<T>(T polygon) where T : ISimpleRing
	{
		double scaledCellSize = _raster.CellSize * 1E-05;
		for (int i = 0; i < polygon.SegmentCount; i++)
		{
			Segment segment = polygon.GetSegment(i);
			double deltaY = segment.DeltaY;
			if (deltaY == 0.0)
			{
				continue;
			}
			double segmentTerm;
			double x;
			double segmentTerm2;
			double x2;
			int ordinal;
			int nextPointIndexPolygonTerm;
			if (deltaY > 0.0)
			{
				segmentTerm = segment.A.Y;
				x = segment.A.X;
				segmentTerm2 = segment.B.Y;
				x2 = segment.B.X;
				ordinal = i;
				nextPointIndexPolygonTerm = polygon.NextPointIndex(ordinal);
			}
			else
			{
				segmentTerm = segment.B.Y;
				x = segment.B.X;
				segmentTerm2 = segment.A.Y;
				x2 = segment.A.X;
				deltaY = 0.0 - deltaY;
				nextPointIndexPolygonTerm = i;
				ordinal = polygon.NextPointIndex(nextPointIndexPolygonTerm);
			}
			if (!_raster.RowsInRange(segmentTerm, segmentTerm2, out var minRow, out var maxRow))
			{
				continue;
			}
			double x2DeltaYTerm = (x2 - x) / deltaY;
			double cellCenterY = _raster.CellCenterY(minRow);
			if (segmentTerm2 == cellCenterY)
			{
				segmentTerm2 -= scaledCellSize;
			}
			if (segmentTerm == cellCenterY)
			{
				segmentTerm -= scaledCellSize;
			}
			List<double> rowData = _rowData[minRow];
			if (segmentTerm2 == cellCenterY)
			{
				if (x2 < _raster.MaxX)
				{
					if (IsVertexVerticallyBetweenNeighbors(polygon, nextPointIndexPolygonTerm))
					{
						if (!rowData.Contains(x2))
						{
							rowData.Add(x2);
						}
					}
					else
					{
						rowData.Remove(x2);
					}
				}
			}
			else if (segmentTerm == cellCenterY)
			{
				if (x < _raster.MaxX)
				{
					if (IsVertexVerticallyBetweenNeighbors(polygon, ordinal))
					{
						if (!rowData.Contains(x))
						{
							rowData.Add(x);
						}
					}
					else
					{
						rowData.Remove(x);
					}
				}
			}
			else if (segmentTerm2 > cellCenterY && segmentTerm < cellCenterY)
			{
				double cellCenterYTerm = x + (cellCenterY - segmentTerm) * x2DeltaYTerm;
				if (cellCenterYTerm < _raster.MaxX)
				{
					rowData.Add(cellCenterYTerm);
				}
			}
			if (maxRow <= minRow)
			{
				continue;
			}
			List<double> rowDataList = _rowData[maxRow];
			double cellCenterY2 = _raster.CellCenterY(maxRow);
			if (segmentTerm == cellCenterY2)
			{
				segmentTerm -= scaledCellSize;
			}
			if (segmentTerm == cellCenterY2)
			{
				if (x < _raster.MaxX)
				{
					if (IsVertexVerticallyBetweenNeighbors(polygon, ordinal))
					{
						if (!rowDataList.Contains(x))
						{
							rowDataList.Add(x);
						}
					}
					else
					{
						rowDataList.Remove(x);
					}
				}
			}
			else if (segmentTerm < cellCenterY2)
			{
				double cellCenterY2Term = x + (cellCenterY2 - segmentTerm) * x2DeltaYTerm;
				if (cellCenterY2Term < _raster.MaxX)
				{
					rowDataList.Add(cellCenterY2Term);
				}
			}
			for (int j = minRow + 1; j < maxRow; j++)
			{
				double cellCenterYRasterTerm = x + (_raster.CellCenterY(j) - segmentTerm) * x2DeltaYTerm;
				if (cellCenterYRasterTerm < _raster.MaxX)
				{
					_rowData[j].Add(cellCenterYRasterTerm);
				}
			}
		}
	}

	private static bool IsVertexVerticallyBetweenNeighbors<T>(T pg, int ptIndex) where T : ISimpleRing
	{
		Span<Point> points = pg.Points.Span;
		double y = points[ptIndex].Y;
		int index = pg.NextPointIndex(ptIndex);
		int index2 = pg.PreviousPointIndex(ptIndex);
		double pointsIndexTerm = points[index].Y - y;
		double pointsIndex2Term = points[index2].Y - y;
		if (pointsIndexTerm == 0.0 && pointsIndex2Term == 0.0)
		{
			return false;
		}
		if (pointsIndexTerm == 0.0)
		{
			return pointsIndex2Term > 0.0;
		}
		if (pointsIndex2Term == 0.0)
		{
			return pointsIndexTerm > 0.0;
		}
		return Math.Sign(pointsIndexTerm) != Math.Sign(pointsIndex2Term);
	}

	public void Replay(Action<int> pixelAction)
	{
		Replay((int row, int startC, int stopC) =>
		{
			int rowTimesCols = row * _raster.Cols;
			for (int i = startC; i <= stopC; i++)
			{
				pixelAction(rowTimesCols + i);
			}
		});
	}

	public bool Replay<T>(T t, Action<int, int, int, T> rowStartStopColAction)
	{
		bool result = false;
		for (int i = _absMinRow; i <= _absMaxRow; i++)
		{
			List<double> rowData = _rowData[i];
			if (rowData.Count == 0)
			{
				continue;
			}
			for (int j = 0; j < rowData.Count; j += 2)
			{
				double x = rowData[j];
				double x2 = rowData[j + 1];
				if (!(x2 > _raster.MinX))
				{
					continue;
				}
				int col;
				if (x < _raster.MinX)
				{
					col = 0;
				}
				else
				{
					col = _raster.Col(in x);
					if (x > _raster.CellCenterX(col))
					{
						col++;
					}
				}
				int colsOffset;
				if (x2 >= _raster.MaxX)
				{
					colsOffset = _raster.Cols - 1;
				}
				else
				{
					colsOffset = _raster.Col(in x2);
					if (x2 < _raster.CellCenterX(colsOffset))
					{
						colsOffset--;
					}
				}
				if (colsOffset >= 0 && col < _raster.Cols)
				{
					rowStartStopColAction(i, col, colsOffset, t);
					result = true;
				}
			}
		}
		return result;
	}

	public void Replay(Action<int, int, int> rowStartStopColAction)
	{
		for (int i = _absMinRow; i <= _absMaxRow; i++)
		{
			List<double> rowData = _rowData[i];
			if (rowData.Count == 0)
			{
				continue;
			}
			for (int j = 0; j < rowData.Count; j += 2)
			{
				double x = rowData[j];
				double x2 = rowData[j + 1];
				if (!(x2 > _raster.MinX))
				{
					continue;
				}
				int col;
				if (x < _raster.MinX)
				{
					col = 0;
				}
				else
				{
					col = _raster.Col(in x);
					if (x > _raster.CellCenterX(col))
					{
						col++;
					}
				}
				int colsOffset;
				if (x2 >= _raster.MaxX)
				{
					colsOffset = _raster.Cols - 1;
				}
				else
				{
					colsOffset = _raster.Col(in x2);
					if (x2 < _raster.CellCenterX(colsOffset))
					{
						colsOffset--;
					}
				}
				rowStartStopColAction(i, col, colsOffset);
			}
		}
	}

	private void SortIntersections()
	{
		for (int i = _absMinRow; i <= _absMaxRow; i++)
		{
			List<double> rowData = _rowData[i];
			if (rowData.Count != 0)
			{
				rowData.Sort();
				if (rowData.Count % 2 > 0)
				{
					rowData.Add(_raster.MaxX);
				}
			}
		}
	}
}
