using System;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RasterizeTriangles
{
	private RasterM _raster;

	public RasterizeTriangles(RasterM raster)
	{
		_raster = raster;
	}

	public void ComputeCells(PointM p0, PointM p1, PointM p2, Action<int, float, float> fillCellAction)
	{
		if ((p0.X < _raster.MinX && p1.X < _raster.MinX && p2.X < _raster.MinX) || (p0.X > _raster.MaxX && p1.X > _raster.MaxX && p2.X > _raster.MaxX) || (p0.Y < _raster.MinY && p1.Y < _raster.MinY && p2.Y < _raster.MinY) || (p0.Y > _raster.MaxY && p1.Y > _raster.MaxY && p2.Y > _raster.MaxY))
		{
			return;
		}
		PlaneM planeM = default;
		planeM.SetCoeffientsPtPtPt(p0, p1, p2);
		double dZ_dXPlaneMTerm = planeM.dZ_dX() * _raster.CellSize;
		double dM_dXPlaneMTerm = planeM.dM_dX() * _raster.CellSize;
		PointM.Sort3PointsByY(ref p0, ref p1, ref p2);
		if (p0.Y == p2.Y)
		{
			return;
		}
		checked
		{
			int row;
			if (p2.Y > _raster.MaxY)
			{
				row = 0;
			}
			else
			{
				row = _raster.Row(p2.Y);
				if (p2.Y < _raster.CellCenterY(row))
				{
					row++;
				}
			}
			int rowsOffset;
			if (p0.Y < _raster.MinY)
			{
				rowsOffset = _raster.Rows - 1;
			}
			else
			{
				rowsOffset = _raster.Row(p0.Y);
				if (p0.Y > _raster.CellCenterY(rowsOffset))
				{
					rowsOffset--;
				}
			}
			if (row > rowsOffset)
			{
				return;
			}
			double p2P0Term = (p2.X - p0.X) / (p2.Y - p0.Y);
			double p2P1Term = (p2.X - p1.X) / (p2.Y - p1.Y);
			double p1P0Term = (p1.X - p0.X) / (p1.Y - p0.Y);
			double cellSizeRasterTerm = p2P0Term * _raster.CellSize;
			double cellSizeRasterTerm2 = p2P1Term * _raster.CellSize;
			double cellSizeRasterTerm3 = p1P0Term * _raster.CellSize;
			double cellCenterY = _raster.CellCenterY(row);
			double p2CellCenterYTerm = p2.X + p2P0Term * (cellCenterY - p2.Y);
			double p2CellCenterYTerm2 = p2.X + p2P1Term * (cellCenterY - p2.Y);
			double p1CellCenterYTerm = p1.X + p1P0Term * (cellCenterY - p1.Y);
			int row2 = row;
			int count = rowsOffset;
			for (int i = row2; i <= count; i++)
			{
				int colsRasterTerm = i * _raster.Cols;
				double n = p2CellCenterYTerm;
				double n2 = ((!(cellCenterY > p1.Y) && !double.IsNaN(p1CellCenterYTerm)) ? p1CellCenterYTerm : p2CellCenterYTerm2);
				if (n > n2)
				{
					RasMapperLib.Utilities.Math.SwapD(ref n, ref n2);
				}
				int col = _raster.Col(n);
				if (_raster.CellCenterX(col) < n)
				{
					col++;
				}
				int col2 = _raster.Col(n2);
				if (_raster.CellCenterX(col2) > n2)
				{
					col2--;
				}
				if (col2 >= 0 && col < _raster.Cols)
				{
					if (col < 0)
					{
						col = 0;
					}
					if (col2 >= _raster.Cols)
					{
						col2 = _raster.Cols - 1;
					}
					n = _raster.CellCenterX(col);
					double planeMCellCenterYTerm = planeM.Z(n, cellCenterY);
					double planeMCellCenterYTerm2 = planeM.M(n, cellCenterY);
					int colStart = col;
					int colEnd = col2;
					for (int j = colStart; j <= colEnd; j++)
					{
						fillCellAction(colsRasterTerm + j, (float)planeMCellCenterYTerm, (float)planeMCellCenterYTerm2);
						planeMCellCenterYTerm += dZ_dXPlaneMTerm;
						planeMCellCenterYTerm2 += dM_dXPlaneMTerm;
					}
				}
				cellCenterY -= _raster.CellSize;
				p2CellCenterYTerm -= cellSizeRasterTerm;
				p2CellCenterYTerm2 -= cellSizeRasterTerm2;
				p1CellCenterYTerm -= cellSizeRasterTerm3;
			}
		}
	}
}
