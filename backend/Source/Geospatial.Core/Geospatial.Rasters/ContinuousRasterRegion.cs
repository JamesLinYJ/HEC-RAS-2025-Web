using System.Collections.Generic;

namespace Geospatial.Rasters;

public class ContinuousRasterRegion
{
	private RasterDefinition _raster;

	private Range1D[] _rowData;

	private Range1D[] _colData;

	private List<Range1D>[] _rowDisincludes;

	private List<Range1D>[] _columnDisincludes;

	public ContinuousRasterRegion(RasterDefinition raster)
	{
		_raster = raster;
		_rowData = new Range1D[raster.Rows];
		_colData = new Range1D[raster.Cols];
		_rowDisincludes = new List<Range1D>[raster.Rows];
		_columnDisincludes = new List<Range1D>[raster.Cols];
		for (int i = 0; i < raster.Cols; i++)
		{
			_colData[i] = Range1D.NullRange;
		}
		for (int j = 0; j < raster.Rows; j++)
		{
			_rowData[j] = Range1D.NullRange;
		}
	}

	public void Include(Pixel px)
	{
		int row = px.Row;
		int column = px.Column;
		Range1D range1D = _rowData[row];
		_ = ref _colData[column];
		if (range1D.IsNull())
		{
			_rowData[row] = new Range1D(column, column);
		}
		else
		{
			if (column == range1D.Start || column == range1D.End)
			{
				return;
			}
			if (column < range1D.Start)
			{
				_rowData[row] = new Range1D(column, range1D.End);
				_ = range1D.Start - column;
				_ = 1;
			}
		}
		_colData[column].IsNull();
	}

	private void DisincludeRow(int row, int startCol, int endCol)
	{
		new Range1D(startCol, endCol);
		List<Range1D> rowDisinclude = _rowDisincludes[row];
		if (rowDisinclude == null)
		{
			rowDisinclude = new List<Range1D>();
			_rowDisincludes[row] = rowDisinclude;
			rowDisinclude.Add(new Range1D(startCol, endCol));
		}
		else
		{
			for (int i = 0; i < rowDisinclude.Count; i++)
			{
				_ = rowDisinclude[i];
			}
		}
	}

	public bool Contains(Pixel px)
	{
		return false;
	}
}
