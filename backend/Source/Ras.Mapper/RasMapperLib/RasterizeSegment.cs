using System;
using System.Runtime.CompilerServices;

namespace RasMapperLib;

public class RasterizeSegment
{
	private RasterM _raster;

	public RasterizeSegment(RasterM raster)
	{
		_raster = raster;
	}

	public void ComputeCells(SegmentM segment, Action<int> fillCellAction)
	{
		ComputeCells(segment, [SpecialName] (int row, int col) =>
		{
			fillCellAction(_raster.CellIndex(row, col));
		});
	}

	public void ComputeCells(SegmentM segment, Action<int, int> fillCellAction, double fpTolerance = 0.0)
	{
		Extent extent = _raster.ComputeExtent();
		if ((!extent.RoughlyContains(segment.A, fpTolerance) || !extent.RoughlyContains(segment.B, fpTolerance)) && !segment.ClipToExtent(_raster))
		{
			return;
		}
		int row = _raster.Row(segment.A.Y);
		int col = _raster.Col(segment.A.X);
		int row2 = _raster.Row(segment.B.Y);
		int col2 = _raster.Col(segment.B.X);
		checked
		{
			if (row < 0)
			{
				row = 0;
			}
			else if (row == _raster.Rows)
			{
				row = _raster.Rows - 1;
			}
			if (col < 0)
			{
				col = 0;
			}
			else if (col == _raster.Cols)
			{
				col = _raster.Cols - 1;
			}
			if (row2 < 0)
			{
				row2 = 0;
			}
			else if (row2 == _raster.Rows)
			{
				row2 = _raster.Rows - 1;
			}
			if (col2 < 0)
			{
				col2 = 0;
			}
			else if (col2 == _raster.Cols)
			{
				col2 = _raster.Cols - 1;
			}
			int col2AboveCol = col2 - col;
			int row2AboveRow = row2 - row;
			double deltaX = segment.DeltaX();
			double deltaY = segment.DeltaY();
			if ((col2AboveCol == 0) & (row2AboveRow == 0))
			{
				fillCellAction(row, col);
				return;
			}
			if (col2AboveCol == 0)
			{
				int row3 = row;
				int row4 = row2;
				int ordinal = Math.Sign(row2AboveRow);
				for (int i = row3; ((ordinal >> 31) ^ i) <= ((ordinal >> 31) ^ row4); i += ordinal)
				{
					fillCellAction(i, col);
				}
				return;
			}
			if (row2AboveRow == 0)
			{
				int col3 = col;
				int col4 = col2;
				int ordinal2 = Math.Sign(col2AboveCol);
				for (int j = col3; ((ordinal2 >> 31) ^ j) <= ((ordinal2 >> 31) ^ col4); j += ordinal2)
				{
					fillCellAction(row, j);
				}
				return;
			}
			int col5 = col;
			int row5 = row;
			fillCellAction(row5, col5);
			if (col2AboveCol > 0)
			{
				if (row2AboveRow > 0)
				{
					while (!((col5 == col2) & (row5 == row2)))
					{
						double cellMinYRasterTerm = (_raster.CellMinY(row5) - segment.A.Y) / deltaY;
						double cellMaxXRasterTerm = (_raster.CellMaxX(col5) - segment.A.X) / deltaX;
						if (((cellMinYRasterTerm < cellMaxXRasterTerm) & (row5 < row2)) | (col5 == col2))
						{
							row5++;
						}
						else
						{
							col5++;
						}
						fillCellAction(row5, col5);
					}
					return;
				}
				while (!((col5 == col2) & (row5 == row2)))
				{
					double cellMaxYRasterTerm = (_raster.CellMaxY(row5) - segment.A.Y) / deltaY;
					double cellMaxXRasterTerm2 = (_raster.CellMaxX(col5) - segment.A.X) / deltaX;
					if (((cellMaxYRasterTerm < cellMaxXRasterTerm2) & (row5 > row2)) | (col5 == col2))
					{
						row5--;
					}
					else
					{
						col5++;
					}
					fillCellAction(row5, col5);
				}
				return;
			}
			if (row2AboveRow > 0)
			{
				while (!((col5 == col2) & (row5 == row2)))
				{
					double cellMinYRasterTerm2 = (_raster.CellMinY(row5) - segment.A.Y) / deltaY;
					double cellMinXRasterTerm = (_raster.CellMinX(col5) - segment.A.X) / deltaX;
					if (((cellMinYRasterTerm2 < cellMinXRasterTerm) & (row5 < row2)) | (col5 == col2))
					{
						row5++;
					}
					else
					{
						col5--;
					}
					fillCellAction(row5, col5);
				}
				return;
			}
			while (!((col5 == col2) & (row5 == row2)))
			{
				double cellMaxYRasterTerm2 = (_raster.CellMaxY(row5) - segment.A.Y) / deltaY;
				double cellMinXRasterTerm2 = (_raster.CellMinX(col5) - segment.A.X) / deltaX;
				if (((cellMaxYRasterTerm2 < cellMinXRasterTerm2) & (row5 > row2)) | (col5 == col2))
				{
					row5--;
				}
				else
				{
					col5--;
				}
				fillCellAction(row5, col5);
			}
		}
	}
}
