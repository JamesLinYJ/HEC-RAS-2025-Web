using System;
using System.Collections.Generic;
using RasMapperLib.Mesh;

namespace RasMapperLib;

public class RasterizePolygon
{
	private RasterM _raster;

	private List<double>[] _rowData;

	public RasterizePolygon(RasterM raster)
	{
		_raster = raster;
		SetupArrays(raster.Rows);
	}

	private void SetupArrays(int rows)
	{
		checked
		{
			_rowData = new List<double>[rows - 1 + 1];
			int count = rows - 1;
			for (int i = 0; i <= count; i++)
			{
				_rowData[i] = new List<double>();
			}
		}
	}

	public void ResetRaster(RasterM raster)
	{
		if (raster.Rows != _raster.Rows)
		{
			SetupArrays(raster.Rows);
		}
		_raster = raster;
	}

	public void ComputeCells(Polygon polygon, Action<int> fillCellAction)
	{
		int row = _raster.Row(Math.Min(polygon.MaxY, _raster.MaxY));
		int row2 = _raster.Row(Math.Max(polygon.MinY, _raster.MinY));
		if (row < 0)
		{
			row = 0;
		}
		checked
		{
			if (row2 >= _raster.Rows)
			{
				row2 = _raster.Rows - 1;
			}
			int rowStart = row;
			int rowEnd = row2;
			for (int i = rowStart; i <= rowEnd; i++)
			{
				_rowData[i].Clear();
			}
			double scaledCellSize = _raster.CellSize * 1E-05;
			int count = polygon.PartsCount() - 1;
			int minRow = default;
			int maxRow = default;
			for (int j = 0; j <= count; j++)
			{
				int count2 = polygon.PartSegmentCount(j) - 1;
				for (int k = 0; k <= count2; k++)
				{
					SegmentM segmentM = polygon.PartSegment(j, k);
					double deltaY = segmentM.DeltaY();
					if (deltaY == 0.0)
					{
						continue;
					}
					double segmentMTerm;
					double x;
					double segmentMTerm2;
					double x2;
					int partStartIndexPolygonTerm;
					int getNextPointOnPartPolygonTerm;
					if (deltaY > 0.0)
					{
						segmentMTerm = segmentM.A.Y;
						x = segmentM.A.X;
						segmentMTerm2 = segmentM.B.Y;
						x2 = segmentM.B.X;
						partStartIndexPolygonTerm = k + polygon.PartStartIndex(j);
						getNextPointOnPartPolygonTerm = polygon.GetNextPointOnPart(j, partStartIndexPolygonTerm);
					}
					else
					{
						segmentMTerm = segmentM.B.Y;
						x = segmentM.B.X;
						segmentMTerm2 = segmentM.A.Y;
						x2 = segmentM.A.X;
						deltaY = 0.0 - deltaY;
						getNextPointOnPartPolygonTerm = k + polygon.PartStartIndex(j);
						partStartIndexPolygonTerm = polygon.GetNextPointOnPart(j, getNextPointOnPartPolygonTerm);
					}
					if (!_raster.RowsInRange(segmentMTerm, segmentMTerm2, ref minRow, ref maxRow))
					{
						continue;
					}
					double x2DeltaYTerm = (x2 - x) / deltaY;
					double cellCenterY = _raster.CellCenterY(minRow);
					if (segmentMTerm2 == cellCenterY)
					{
						segmentMTerm2 -= scaledCellSize;
					}
					if (segmentMTerm == cellCenterY)
					{
						segmentMTerm -= scaledCellSize;
					}
					if (segmentMTerm2 == cellCenterY)
					{
						if (x2 < _raster.MaxX)
						{
							if (polygon.IsVertexVerticallyBetweenNeighbors(j, getNextPointOnPartPolygonTerm))
							{
								if (!_rowData[minRow].Contains(x2))
								{
									_rowData[minRow].Add(x2);
								}
							}
							else
							{
								_rowData[minRow].Remove(x2);
							}
						}
					}
					else if (segmentMTerm == cellCenterY)
					{
						if (x < _raster.MaxX)
						{
							if (polygon.IsVertexVerticallyBetweenNeighbors(j, partStartIndexPolygonTerm))
							{
								if (!_rowData[minRow].Contains(x))
								{
									_rowData[minRow].Add(x);
								}
							}
							else
							{
								_rowData[minRow].Remove(x);
							}
						}
					}
					else if (segmentMTerm2 > cellCenterY && segmentMTerm < cellCenterY)
					{
						double cellCenterYTerm = x + (cellCenterY - segmentMTerm) * x2DeltaYTerm;
						if (cellCenterYTerm < _raster.MaxX)
						{
							_rowData[minRow].Add(cellCenterYTerm);
						}
					}
					if (maxRow <= minRow)
					{
						continue;
					}
					double cellCenterY2 = _raster.CellCenterY(maxRow);
					if (segmentMTerm == cellCenterY2)
					{
						segmentMTerm -= scaledCellSize;
					}
					if (segmentMTerm == cellCenterY2)
					{
						if (x < _raster.MaxX)
						{
							if (polygon.IsVertexVerticallyBetweenNeighbors(j, partStartIndexPolygonTerm))
							{
								if (!_rowData[maxRow].Contains(x))
								{
									_rowData[maxRow].Add(x);
								}
							}
							else
							{
								_rowData[maxRow].Remove(x);
							}
						}
					}
					else if (segmentMTerm < cellCenterY2)
					{
						double cellCenterY2Term = x + (cellCenterY2 - segmentMTerm) * x2DeltaYTerm;
						if (cellCenterY2Term < _raster.MaxX)
						{
							_rowData[maxRow].Add(cellCenterY2Term);
						}
					}
					int minRowOffset = minRow + 1;
					int count3 = maxRow - 1;
					for (int l = minRowOffset; l <= count3; l++)
					{
						double cellCenterYRasterTerm = x + (_raster.CellCenterY(l) - segmentMTerm) * x2DeltaYTerm;
						if (cellCenterYRasterTerm < _raster.MaxX)
						{
							_rowData[l].Add(cellCenterYRasterTerm);
						}
					}
				}
			}
			int rowStart2 = row;
			int rowEnd2 = row2;
			for (int m = rowStart2; m <= rowEnd2; m++)
			{
				if (_rowData[m].Count == 0)
				{
					continue;
				}
				_rowData[m].Sort();
				if (unchecked(_rowData[m].Count % 2) != 0)
				{
					_rowData[m].Add(_raster.MaxX);
				}
				int colsRasterTerm = m * _raster.Cols;
				int count4 = _rowData[m].Count - 2;
				for (int n = 0; n <= count4; n += 2)
				{
					double rowData = _rowData[m][n];
					double rowData2 = _rowData[m][n + 1];
					if (!(rowData2 > _raster.MinX))
					{
						continue;
					}
					int col;
					if (rowData < _raster.MinX)
					{
						col = 0;
					}
					else
					{
						col = _raster.Col(rowData);
						if (rowData > _raster.CellCenterX(col))
						{
							col++;
						}
					}
					int colsOffset;
					if (rowData2 >= _raster.MaxX)
					{
						colsOffset = _raster.Cols - 1;
					}
					else
					{
						colsOffset = _raster.Col(rowData2);
						if (rowData2 < _raster.CellCenterX(colsOffset))
						{
							colsOffset--;
						}
					}
					int col2 = col;
					int count5 = colsOffset;
					for (int col3 = col2; col3 <= count5; col3++)
					{
						fillCellAction(colsRasterTerm + col3);
					}
				}
			}
		}
	}

	public void ComputeCells(List<SegmentM> segments, Action<int> fillCellAction)
	{
		double minValue = double.MinValue;
		double maxValue = double.MaxValue;
		foreach (SegmentM segment in segments)
		{
			minValue = Math.Max(minValue, segment.MaxY);
			maxValue = Math.Min(maxValue, segment.MinY);
		}
		int row = _raster.Row(Math.Min(minValue, _raster.MaxY));
		int row2 = _raster.Row(Math.Max(maxValue, _raster.MinY));
		if (row < 0)
		{
			row = 0;
		}
		checked
		{
			if (row2 >= _raster.Rows)
			{
				row2 = _raster.Rows - 1;
			}
			int rowStart = row;
			int rowEnd = row2;
			for (int i = rowStart; i <= rowEnd; i++)
			{
				_rowData[i].Clear();
			}
			int count = segments.Count - 1;
			int minRow = default;
			int maxRow = default;
			for (int j = 0; j <= count; j++)
			{
				SegmentM segmentM = segments[j];
				double deltaY = segmentM.DeltaY();
				double y;
				double x;
				double y2;
				double x2;
				if (deltaY > 0.0)
				{
					y = segmentM.A.Y;
					x = segmentM.A.X;
					y2 = segmentM.B.Y;
					x2 = segmentM.B.X;
				}
				else
				{
					y = segmentM.B.Y;
					x = segmentM.B.X;
					y2 = segmentM.A.Y;
					x2 = segmentM.A.X;
					deltaY = 0.0 - deltaY;
				}
				if (deltaY == 0.0 || !_raster.RowsInRange(y, y2, ref minRow, ref maxRow))
				{
					continue;
				}
				double x2DeltaYTerm = (x2 - x) / deltaY;
				double cellCenterY = _raster.CellCenterY(minRow);
				if (y2 == cellCenterY)
				{
					if (x2 < _raster.MaxX && !_rowData[minRow].Contains(x2))
					{
						_rowData[minRow].Add(x2);
					}
				}
				else if (y2 > cellCenterY && y < cellCenterY)
				{
					double cellCenterYTerm = x + (cellCenterY - y) * x2DeltaYTerm;
					if (cellCenterYTerm < _raster.MaxX)
					{
						_rowData[minRow].Add(cellCenterYTerm);
					}
				}
				if (maxRow <= minRow)
				{
					continue;
				}
				double cellCenterY2 = _raster.CellCenterY(maxRow);
				if (y == cellCenterY2)
				{
					if (x < _raster.MaxX && !_rowData[maxRow].Contains(x))
					{
						_rowData[maxRow].Add(x);
					}
				}
				else if (y < cellCenterY2)
				{
					double cellCenterY2Term = x + (cellCenterY2 - y) * x2DeltaYTerm;
					if (cellCenterY2Term < _raster.MaxX)
					{
						_rowData[maxRow].Add(cellCenterY2Term);
					}
				}
				int minRowOffset = minRow + 1;
				int count2 = maxRow - 1;
				for (int k = minRowOffset; k <= count2; k++)
				{
					double cellCenterYRasterTerm = x + (_raster.CellCenterY(k) - y) * x2DeltaYTerm;
					if (cellCenterYRasterTerm < _raster.MaxX)
					{
						_rowData[k].Add(cellCenterYRasterTerm);
					}
				}
			}
			int rowStart2 = row;
			int rowEnd2 = row2;
			for (int l = rowStart2; l <= rowEnd2; l++)
			{
				if (_rowData[l].Count == 0)
				{
					continue;
				}
				_rowData[l].Sort();
				if (unchecked(_rowData[l].Count % 2) != 0)
				{
					_rowData[l].Add(_raster.MaxX);
				}
				int colsRasterTerm = l * _raster.Cols;
				int count3 = _rowData[l].Count - 2;
				for (int m = 0; m <= count3; m += 2)
				{
					double rowData = _rowData[l][m];
					double rowData2 = _rowData[l][m + 1];
					if (!(rowData2 > _raster.MinX))
					{
						continue;
					}
					int col;
					if (rowData < _raster.MinX)
					{
						col = 0;
					}
					else
					{
						col = _raster.Col(rowData);
						if (rowData > _raster.CellCenterX(col))
						{
							col++;
						}
					}
					int colsOffset;
					if (rowData2 >= _raster.MaxX)
					{
						colsOffset = _raster.Cols - 1;
					}
					else
					{
						colsOffset = _raster.Col(rowData2);
						if (rowData2 < _raster.CellCenterX(colsOffset))
						{
							colsOffset--;
						}
					}
					int col2 = col;
					int count4 = colsOffset;
					for (int n = col2; n <= count4; n++)
					{
						fillCellAction(colsRasterTerm + n);
					}
				}
			}
		}
	}

	public void ComputeCells(List<Segment2Double> segments, Action<int> fillCellAction)
	{
		double minValue = double.MinValue;
		double maxValue = double.MaxValue;
		checked
		{
			int count = segments.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Segment2Double segment2Double = segments[i];
				minValue = Math.Max(minValue, segment2Double.MaxY());
				maxValue = Math.Min(maxValue, segment2Double.MinY());
			}
			int row = _raster.Row(Math.Min(minValue, _raster.MaxY));
			int row2 = _raster.Row(Math.Max(maxValue, _raster.MinY));
			if (row < 0)
			{
				row = 0;
			}
			if (row2 >= _raster.Rows)
			{
				row2 = _raster.Rows - 1;
			}
			int rowStart = row;
			int rowEnd = row2;
			for (int j = rowStart; j <= rowEnd; j++)
			{
				_rowData[j].Clear();
			}
			int count2 = segments.Count - 1;
			int minRow = default;
			int maxRow = default;
			for (int k = 0; k <= count2; k++)
			{
				Segment2Double segment2Double2 = segments[k];
				double deltaY = segment2Double2.DeltaY();
				double y;
				double x;
				double y2;
				double x2;
				if (deltaY > 0.0)
				{
					y = segment2Double2.A.Y;
					x = segment2Double2.A.X;
					y2 = segment2Double2.B.Y;
					x2 = segment2Double2.B.X;
				}
				else
				{
					y = segment2Double2.B.Y;
					x = segment2Double2.B.X;
					y2 = segment2Double2.A.Y;
					x2 = segment2Double2.A.X;
					deltaY = 0.0 - deltaY;
				}
				if (deltaY == 0.0 || !_raster.RowsInRange(y, y2, ref minRow, ref maxRow))
				{
					continue;
				}
				double x2DeltaYTerm = (x2 - x) / deltaY;
				double cellCenterY = _raster.CellCenterY(minRow);
				if (y2 == cellCenterY)
				{
					if (x2 < _raster.MaxX && !_rowData[minRow].Contains(x2))
					{
						_rowData[minRow].Add(x2);
					}
				}
				else if (y2 > cellCenterY && y < cellCenterY)
				{
					double cellCenterYTerm = x + (cellCenterY - y) * x2DeltaYTerm;
					if (cellCenterYTerm < _raster.MaxX)
					{
						_rowData[minRow].Add(cellCenterYTerm);
					}
				}
				if (maxRow <= minRow)
				{
					continue;
				}
				double cellCenterY2 = _raster.CellCenterY(maxRow);
				if (y == cellCenterY2)
				{
					if (x < _raster.MaxX && !_rowData[maxRow].Contains(x))
					{
						_rowData[maxRow].Add(x);
					}
				}
				else if (y < cellCenterY2)
				{
					double cellCenterY2Term = x + (cellCenterY2 - y) * x2DeltaYTerm;
					if (cellCenterY2Term < _raster.MaxX)
					{
						_rowData[maxRow].Add(cellCenterY2Term);
					}
				}
				int minRowOffset = minRow + 1;
				int count3 = maxRow - 1;
				for (int l = minRowOffset; l <= count3; l++)
				{
					double cellCenterYRasterTerm = x + (_raster.CellCenterY(l) - y) * x2DeltaYTerm;
					if (cellCenterYRasterTerm < _raster.MaxX)
					{
						_rowData[l].Add(cellCenterYRasterTerm);
					}
				}
			}
			int rowStart2 = row;
			int rowEnd2 = row2;
			for (int m = rowStart2; m <= rowEnd2; m++)
			{
				if (_rowData[m].Count == 0)
				{
					continue;
				}
				_rowData[m].Sort();
				if (unchecked(_rowData[m].Count % 2) != 0)
				{
					_rowData[m].Add(_raster.MaxX);
				}
				int colsRasterTerm = m * _raster.Cols;
				int count4 = _rowData[m].Count - 2;
				for (int n = 0; n <= count4; n += 2)
				{
					double rowData = _rowData[m][n];
					double rowData2 = _rowData[m][n + 1];
					if (!(rowData2 > _raster.MinX))
					{
						continue;
					}
					int col;
					if (rowData < _raster.MinX)
					{
						col = 0;
					}
					else
					{
						col = _raster.Col(rowData);
						if (rowData > _raster.CellCenterX(col))
						{
							col++;
						}
					}
					int colsOffset;
					if (rowData2 >= _raster.MaxX)
					{
						colsOffset = _raster.Cols - 1;
					}
					else
					{
						colsOffset = _raster.Col(rowData2);
						if (rowData2 < _raster.CellCenterX(colsOffset))
						{
							colsOffset--;
						}
					}
					int col2 = col;
					int count5 = colsOffset;
					for (int col3 = col2; col3 <= count5; col3++)
					{
						fillCellAction(colsRasterTerm + col3);
					}
				}
			}
		}
	}
}
