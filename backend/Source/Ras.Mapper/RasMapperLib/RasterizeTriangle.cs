using System;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class RasterizeTriangle
{
	private struct BaryDelta
	{
		public double StartValue;

		public double DeltaX;

		public double DeltaY;
	}

	private struct BarycentricData
	{
		public double originW;

		public double dw_col;

		public double dw_row;

		public BarycentricData(double a, double b, double c, double orginDeltaX, double originDeltaY, double cellSize)
		{
			this = default;
			double computed = (0.0 - a) / c;
			double computed2 = (0.0 - b) / c;
			originW = 1.0 - computed * orginDeltaX - computed2 * originDeltaY;
			dw_col = computed * cellSize;
			dw_row = computed2 * cellSize;
		}
	}

	private RasterM _raster;

	public RasterM Raster => _raster;

	public RasterizeTriangle(RasterM raster)
	{
		_raster = raster;
	}

	public void ComputeCellsZMandDirection(PointM p0, PointM p1, PointM p2, Action<int, float, float, float, float> fillCellAction)
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
			float systemTerm;
			float unity;
			float planeMTerm;
			if (System.Math.Abs(planeM.F) > System.Math.Abs(planeM.E))
			{
				unity = 1f;
				planeMTerm = (float)((0.0 - planeM.E) / planeM.F);
				systemTerm = (float)System.Math.Sqrt(1f + planeMTerm * planeMTerm);
			}
			else
			{
				planeMTerm = 1f;
				unity = (float)((0.0 - planeM.F) / planeM.E);
				systemTerm = (float)System.Math.Sqrt(1f + unity * unity);
			}
			unity /= systemTerm;
			planeMTerm /= systemTerm;
			if ((planeM.A * (double)unity + planeM.B * (double)planeMTerm) * planeM.C < 0.0)
			{
				unity = 0f - unity;
				planeMTerm = 0f - planeMTerm;
			}
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
						fillCellAction(colsRasterTerm + j, (float)planeMCellCenterYTerm, (float)planeMCellCenterYTerm2, unity, planeMTerm);
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

	public void ComputeCellsZM(PointM p0, PointM p1, PointM p2, Action<int, float, float> fillCellAction)
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

	public void ComputeCellsZ(PointM p0, PointM p1, PointM p2, Action<int, float> fillCellAction)
	{
		if ((p0.X < _raster.MinX && p1.X < _raster.MinX && p2.X < _raster.MinX) || (p0.X > _raster.MaxX && p1.X > _raster.MaxX && p2.X > _raster.MaxX) || (p0.Y < _raster.MinY && p1.Y < _raster.MinY && p2.Y < _raster.MinY) || (p0.Y > _raster.MaxY && p1.Y > _raster.MaxY && p2.Y > _raster.MaxY))
		{
			return;
		}
		PlaneM planeM = default;
		planeM.SetCoeffientsPtPtPt(p0, p1, p2);
		double dZ_dXPlaneMTerm = planeM.dZ_dX() * _raster.CellSize;
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
					int colStart = col;
					int colEnd = col2;
					for (int j = colStart; j <= colEnd; j++)
					{
						fillCellAction(colsRasterTerm + j, (float)planeMCellCenterYTerm);
						planeMCellCenterYTerm += dZ_dXPlaneMTerm;
					}
				}
				cellCenterY -= _raster.CellSize;
				p2CellCenterYTerm -= cellSizeRasterTerm;
				p2CellCenterYTerm2 -= cellSizeRasterTerm2;
				p1CellCenterYTerm -= cellSizeRasterTerm3;
			}
		}
	}

	public void ComputeCells(PointM p0, PointM p1, PointM p2, Action<int> fillCellAction)
	{
		if ((p0.X < _raster.MinX && p1.X < _raster.MinX && p2.X < _raster.MinX) || (p0.X > _raster.MaxX && p1.X > _raster.MaxX && p2.X > _raster.MaxX) || (p0.Y < _raster.MinY && p1.Y < _raster.MinY && p2.Y < _raster.MinY) || (p0.Y > _raster.MaxY && p1.Y > _raster.MaxY && p2.Y > _raster.MaxY))
		{
			return;
		}
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
					int colStart = col;
					int colEnd = col2;
					for (int j = colStart; j <= colEnd; j++)
					{
						fillCellAction(colsRasterTerm + j);
					}
				}
				cellCenterY -= _raster.CellSize;
				p2CellCenterYTerm -= cellSizeRasterTerm;
				p2CellCenterYTerm2 -= cellSizeRasterTerm2;
				p1CellCenterYTerm -= cellSizeRasterTerm3;
			}
		}
	}

	public void ComputeCellsZNew(PointM p0, PointM p1, PointM p2, Action<int, float> fillCellAction)
	{
		if ((p0.Y == p1.Y && p1.Y == p2.Y) || (p0.X == p1.X && p1.X == p2.X))
		{
			return;
		}
		double min3 = RasMapperLib.Utilities.Math.Min3(p0.X, p1.X, p2.X);
		if (min3 > _raster.MaxX)
		{
			return;
		}
		double max3 = RasMapperLib.Utilities.Math.Max3(p0.X, p1.X, p2.X);
		if (max3 < _raster.MinX)
		{
			return;
		}
		double min32 = RasMapperLib.Utilities.Math.Min3(p0.Y, p1.Y, p2.Y);
		if (min32 > _raster.MaxY)
		{
			return;
		}
		double max32 = RasMapperLib.Utilities.Math.Max3(p0.Y, p1.Y, p2.Y);
		if (max32 < _raster.MinY)
		{
			return;
		}
		int colStart = ((!(min3 < _raster.MinX)) ? _raster.Col(min3) : 0);
		checked
		{
			int row = ((!(min32 < _raster.MinY)) ? _raster.Row(min32) : (_raster.Rows - 1));
			int col = ((!(max3 > _raster.MaxX)) ? _raster.Col(max3) : (_raster.Cols - 1));
			int row2 = ((!(max32 > _raster.MaxY)) ? _raster.Row(max32) : 0);
			PointM origin = new PointM(_raster.CellCenterX(colStart), _raster.CellCenterY(row));
			BaryDelta bary = GetBary(p1, p2, origin);
			BaryDelta bary2 = GetBary(p2, p0, origin);
			BaryDelta bary3 = GetBary(p0, p1, origin);
			double deltaXTimesCellSize = bary.DeltaX * _raster.CellSize;
			double deltaXTimesCellSize2 = bary2.DeltaX * _raster.CellSize;
			double deltaXTimesCellSize3 = bary3.DeltaX * _raster.CellSize;
			double deltaYTimesCellSize = bary.DeltaY * _raster.CellSize;
			double deltaYTimesCellSize2 = bary2.DeltaY * _raster.CellSize;
			double deltaYTimesCellSize3 = bary3.DeltaY * _raster.CellSize;
			double startValueBaryTerm = bary.StartValue + bary2.StartValue + bary3.StartValue;
			int row3 = row2;
			for (int i = row; i >= row3; i += -1)
			{
				double startValue = bary.StartValue;
				double startValue2 = bary2.StartValue;
				double startValue3 = bary3.StartValue;
				int colEnd = col;
				for (int j = colStart; j <= colEnd; j++)
				{
					if (startValue > 0.0 && startValue2 > 0.0 && startValue3 > 0.0)
					{
						double startValueP0Term = (startValue * p0.Z + startValue2 * p1.Z + startValue3 * p2.Z) / startValueBaryTerm;
						fillCellAction(i * _raster.Cols + j, (float)startValueP0Term);
					}
					startValue += deltaXTimesCellSize;
					startValue2 += deltaXTimesCellSize2;
					startValue3 += deltaXTimesCellSize3;
				}
				bary.StartValue += deltaYTimesCellSize;
				bary2.StartValue += deltaYTimesCellSize2;
				bary3.StartValue += deltaYTimesCellSize3;
			}
		}
	}

	public void ComputeCellsBary(PointM p0, PointM p1, PointM p2, Action<int, float, float, float> fillCellAction)
	{
		if ((p0.Y == p1.Y && p1.Y == p2.Y) || (p0.X == p1.X && p1.X == p2.X))
		{
			return;
		}
		double min3 = RasMapperLib.Utilities.Math.Min3(p0.X, p1.X, p2.X);
		if (min3 > _raster.MaxX)
		{
			return;
		}
		double max3 = RasMapperLib.Utilities.Math.Max3(p0.X, p1.X, p2.X);
		if (max3 < _raster.MinX)
		{
			return;
		}
		double min32 = RasMapperLib.Utilities.Math.Min3(p0.Y, p1.Y, p2.Y);
		if (min32 > _raster.MaxY)
		{
			return;
		}
		double max32 = RasMapperLib.Utilities.Math.Max3(p0.Y, p1.Y, p2.Y);
		if (max32 < _raster.MinY)
		{
			return;
		}
		int colStart = ((!(min3 < _raster.MinX)) ? _raster.Col(min3) : 0);
		checked
		{
			int row = ((!(min32 < _raster.MinY)) ? _raster.Row(min32) : (_raster.Rows - 1));
			int col = ((!(max3 > _raster.MaxX)) ? _raster.Col(max3) : (_raster.Cols - 1));
			int row2 = ((!(max32 > _raster.MaxY)) ? _raster.Row(max32) : 0);
			PointM origin = new PointM(_raster.CellCenterX(colStart), _raster.CellCenterY(row));
			BaryDelta bary = GetBary(p1, p2, origin);
			BaryDelta bary2 = GetBary(p2, p0, origin);
			BaryDelta bary3 = GetBary(p0, p1, origin);
			double deltaXTimesCellSize = bary.DeltaX * _raster.CellSize;
			double deltaXTimesCellSize2 = bary2.DeltaX * _raster.CellSize;
			double deltaXTimesCellSize3 = bary3.DeltaX * _raster.CellSize;
			double deltaYTimesCellSize = bary.DeltaY * _raster.CellSize;
			double deltaYTimesCellSize2 = bary2.DeltaY * _raster.CellSize;
			double deltaYTimesCellSize3 = bary3.DeltaY * _raster.CellSize;
			double startValueBaryTerm = bary.StartValue + bary2.StartValue + bary3.StartValue;
			int row3 = row2;
			for (int i = row; i >= row3; i += -1)
			{
				double startValue = bary.StartValue;
				double startValue2 = bary2.StartValue;
				double startValue3 = bary3.StartValue;
				int colEnd = col;
				for (int j = colStart; j <= colEnd; j++)
				{
					if (startValue > 0.0 && startValue2 > 0.0 && startValue3 > 0.0)
					{
						fillCellAction(i * _raster.Cols + j, (float)(startValue / startValueBaryTerm), (float)(startValue2 / startValueBaryTerm), (float)(startValue3 / startValueBaryTerm));
					}
					startValue += deltaXTimesCellSize;
					startValue2 += deltaXTimesCellSize2;
					startValue3 += deltaXTimesCellSize3;
				}
				bary.StartValue += deltaYTimesCellSize;
				bary2.StartValue += deltaYTimesCellSize2;
				bary3.StartValue += deltaYTimesCellSize3;
			}
		}
	}

	private BaryDelta GetBary(PointM v0, PointM v1, PointM origin)
	{
		double deltaX = v0.Y - v1.Y;
		double deltaY = v1.X - v0.X;
		double v0V1Term = v0.X * v1.Y - v0.Y * v1.X;
		BaryDelta result = default;
		result.DeltaX = deltaX;
		result.DeltaY = deltaY;
		result.StartValue = deltaX * origin.X + deltaY * origin.Y + v0V1Term;
		return result;
	}

	public void ComputeCellsBarycentric(PointM p0, PointM p1, PointM p2, Action<int, float, float, float> fillCellAction)
	{
		if ((p0.Y == p1.Y && p1.Y == p2.Y) || (p0.X == p1.X && p1.X == p2.X))
		{
			return;
		}
		double max = default;
		double min = default;
		RasMapperLib.Utilities.Math.MaxMin3(p0.X, p1.X, p2.X, ref max, ref min);
		if (max < _raster.MinX || min > _raster.MaxX)
		{
			return;
		}
		double max2 = default;
		double min2 = default;
		RasMapperLib.Utilities.Math.MaxMin3(p0.Y, p1.Y, p2.Y, ref max2, ref min2);
		if (min2 > _raster.MaxY || max2 < _raster.MinY)
		{
			return;
		}
		int colStart = ((!(min < _raster.MinX)) ? _raster.Col(min) : 0);
		checked
		{
			int col = ((!(max > _raster.MaxX)) ? _raster.Col(max) : (_raster.Cols - 1));
			int rowStart = ((!(max2 > _raster.MaxY)) ? _raster.Row(max2) : 0);
			int row = ((!(min2 < _raster.MinY)) ? _raster.Row(min2) : (_raster.Rows - 1));
			double cellCenterX = _raster.CellCenterX(colStart);
			double cellCenterY = _raster.CellCenterY(rowStart);
			double p1P0Term = p1.X - p0.X;
			double p1P0Term2 = p1.Y - p0.Y;
			double p2P0Term = p2.X - p0.X;
			double p2P0Term2 = p2.Y - p0.Y;
			double computed = p1P0Term * p2P0Term2 - p1P0Term2 * p2P0Term;
			if (computed == 0.0)
			{
				return;
			}
			double unity = -1.0;
			double unity2 = -1.0;
			BarycentricData barycentricData = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p0.X - cellCenterX, p0.Y - cellCenterY, _raster.CellSize);
			unity = 1.0;
			unity2 = 0.0;
			BarycentricData barycentricData2 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p1.X - cellCenterX, p1.Y - cellCenterY, _raster.CellSize);
			unity = 0.0;
			unity2 = 1.0;
			BarycentricData barycentricData3 = new BarycentricData(p1P0Term2 * unity2 - unity * p2P0Term2, 0.0 - (p1P0Term * unity2 - unity * p2P0Term), computed, p2.X - cellCenterX, p2.Y - cellCenterY, _raster.CellSize);
			double originW = barycentricData.originW;
			double originW2 = barycentricData2.originW;
			double originW3 = barycentricData3.originW;
			int rowEnd = row;
			for (int i = rowStart; i <= rowEnd; i++)
			{
				double originW4 = originW;
				double originW5 = originW2;
				double originW6 = originW3;
				int colsRasterTerm = i * _raster.Cols + colStart;
				int colEnd = col;
				for (int j = colStart; j <= colEnd; j++)
				{
					if (originW4 > -1E-09 && originW5 > -1E-09 && originW6 > -1E-09)
					{
						fillCellAction(colsRasterTerm, (float)originW4, (float)originW5, (float)originW6);
					}
					colsRasterTerm++;
					originW4 += barycentricData.dw_col;
					originW5 += barycentricData2.dw_col;
					originW6 += barycentricData3.dw_col;
				}
				originW -= barycentricData.dw_row;
				originW2 -= barycentricData2.dw_row;
				originW3 -= barycentricData3.dw_row;
			}
		}
	}

	private void test()
	{
		PointM pointM = new PointM(0.0, 0.0);
		PointM pointM2 = new PointM(10.0, 0.0);
		PointM pointM3 = new PointM(0.0, 10.0);
		PointM origin = new PointM(0.0, 0.0);
		BaryDelta bary = GetBary(pointM2, pointM3, origin);
		BaryDelta bary2 = GetBary(pointM3, pointM, origin);
		BaryDelta bary3 = GetBary(pointM, pointM2, origin);
		int ordinal = 0;
		checked
		{
			do
			{
				double startValue = bary.StartValue;
				double startValue2 = bary2.StartValue;
				double startValue3 = bary3.StartValue;
				int ordinal2 = 0;
				do
				{
					if (startValue >= 0.0 && startValue2 >= 0.0)
					{
						_ = 0.0;
					}
					startValue += bary.DeltaX;
					startValue2 += bary2.DeltaX;
					startValue3 += bary3.DeltaX;
					ordinal2++;
				}
				while (ordinal2 <= 10);
				bary.StartValue += bary.DeltaY;
				bary2.StartValue += bary2.DeltaY;
				bary3.StartValue += bary3.DeltaY;
				ordinal++;
			}
			while (ordinal <= 10);
		}
	}
}
