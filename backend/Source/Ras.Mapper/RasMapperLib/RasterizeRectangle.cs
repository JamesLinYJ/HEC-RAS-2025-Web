using System;
using System.Collections.Generic;

namespace RasMapperLib;

public class RasterizeRectangle
{
	private RasterM _raster;

	public RasterizeRectangle(RasterM raster)
	{
		_raster = raster;
	}

	public void ComputeCells(Extent ext, Action<int> fillCellAction)
	{
		int left = default;
		int right = default;
		int top = default;
		int bot = default;
		if (!ComputeCells(ext, ref left, ref right, ref top, ref bot))
		{
			return;
		}
		int top2 = top;
		int bot2 = bot;
		checked
		{
			for (int i = top2; i <= bot2; i++)
			{
				int left2 = left;
				int right2 = right;
				for (int j = left2; j <= right2; j++)
				{
					fillCellAction(_raster.CellIndex(i, j));
				}
			}
		}
	}

	public bool ComputeCells(List<Point2Double> fpLocations, ref int leftIdx, ref int rightIdx, ref int topIdx, ref int botIdx)
	{
		double minX = Math.Min(fpLocations[0].X, fpLocations[2].X);
		double maxY = Math.Max(fpLocations[0].Y, fpLocations[2].Y);
		double maxX = Math.Max(fpLocations[0].X, fpLocations[2].X);
		double minY = Math.Min(fpLocations[0].Y, fpLocations[2].Y);
		Extent ext = new Extent(maxX, minX, maxY, minY);
		return ComputeCells(ext, ref leftIdx, ref rightIdx, ref topIdx, ref botIdx);
	}

	public bool ComputeCells(Extent ext, ref int left, ref int right, ref int top, ref int bot)
	{
		left = _raster.Col(ext.Left);
		checked
		{
			if (ext.Left > _raster.CellCenterX(left))
			{
				left++;
			}
			right = _raster.Col(ext.Right);
			if (ext.Right < _raster.CellCenterX(right))
			{
				right--;
			}
			top = _raster.Row(ext.Top);
			if (ext.Top < _raster.CellCenterY(top))
			{
				top++;
			}
			bot = _raster.Row(ext.Bottom);
			if (ext.Bottom > _raster.CellCenterY(bot))
			{
				bot--;
			}
			if (left >= _raster.Cols)
			{
				return false;
			}
			if (right < 0)
			{
				return false;
			}
			if (bot < 0)
			{
				return false;
			}
			if (top >= _raster.Rows)
			{
				return false;
			}
			left = Math.Max(0, left);
			right = Math.Min(_raster.Cols - 1, right);
			top = Math.Max(0, top);
			bot = Math.Min(_raster.Rows - 1, bot);
			return true;
		}
	}
}
