using System.Collections.Generic;

namespace RasMapperLib.Terrain;

public class RenderBlock
{
	private RasterM _tile;

	private float[] _tileValues;

	private float[] _perimeterValues;

	private byte[] _mask;

	private List<SegmentM> _stitches;

	private int _maxRow;

	private int _maxCol;

	private float _a;

	private float _b;

	private float _c;

	private float _d;

	private float _e2;

	public List<SegmentM> Stitches => _stitches;

	public void SetTile(RasterM tile, float[] tileValues, float[] perimeterValues, byte[] mask, List<SegmentM> stitches)
	{
		_tile = tile;
		_tileValues = tileValues;
		_perimeterValues = perimeterValues;
		_mask = mask;
		_stitches = stitches;
		checked
		{
			_maxRow = tile.Rows - 1;
			_maxCol = tile.Cols - 1;
		}
	}

	public bool SetRowCol(int rbRow, int rbCol)
	{
		checked
		{
			int rbColRbRowTerm = rbCol + rbRow * _tile.Cols;
			byte b = _mask[rbColRbRowTerm];
			if ((b & 1) != 0)
			{
				_a = _tileValues[rbColRbRowTerm];
				if (rbCol == _maxCol)
				{
					if (rbRow == _maxRow)
					{
						_b = _perimeterValues[rbRow];
						_c = _perimeterValues[rbCol + _tile.Cols + 1];
						_d = _perimeterValues[rbCol + _tile.Cols];
					}
					else
					{
						_b = _perimeterValues[rbRow];
						_c = _perimeterValues[rbRow + 1];
						_d = _tileValues[rbColRbRowTerm + _tile.Cols];
					}
				}
				else if (rbRow == _maxRow)
				{
					_b = _tileValues[rbColRbRowTerm + 1];
					_c = _perimeterValues[rbCol + _tile.Rows + 1];
					_d = _perimeterValues[rbCol + _tile.Rows];
				}
				else
				{
					_b = _tileValues[rbColRbRowTerm + 1];
					_c = _tileValues[rbColRbRowTerm + _tile.Cols + 1];
					_d = _tileValues[rbColRbRowTerm + _tile.Cols];
				}
				_e2 = (_a + _b + _c + _d) * 0.5f;
				if (_stitches != null)
				{
					int ordinal = 0;
					if ((b & 8) != 0)
					{
						ordinal++;
					}
					if ((b & 0x10) != 0)
					{
						ordinal++;
					}
					if ((b & 4) != 0)
					{
						ordinal++;
					}
					if ((b & 2) != 0)
					{
						ordinal++;
					}
					if (ordinal > 0)
					{
						int argIndex = ((ordinal > 1) ? 1 : 0);
						if ((b & 8) != 0)
						{
							_stitches.Add(new SegmentM(PointD(rbRow, rbCol), PointA(rbRow, rbCol), argIndex));
						}
						if ((b & 0x10) != 0)
						{
							_stitches.Add(new SegmentM(PointA(rbRow, rbCol), PointB(rbRow, rbCol), argIndex));
						}
						if ((b & 4) != 0)
						{
							_stitches.Add(new SegmentM(PointB(rbRow, rbCol), PointC(rbRow, rbCol), argIndex));
						}
						if ((b & 2) != 0)
						{
							_stitches.Add(new SegmentM(PointC(rbRow, rbCol), PointD(rbRow, rbCol), argIndex));
						}
					}
				}
				return true;
			}
			return false;
		}
	}

	public float Interpolate(float xfrac, float yfrac)
	{
		float yfracOffset = 1f - yfrac;
		if (xfrac > yfrac)
		{
			if (xfrac < yfracOffset)
			{
				float computed = _b - _a;
				float e2Term = _e2 - (_a + _b);
				return _a + computed * xfrac + e2Term * yfrac;
			}
			float e2Term2 = _e2 - (_b + _c);
			float computed2 = _c - _b;
			return _b + e2Term2 * (1f - xfrac) + computed2 * yfrac;
		}
		if (xfrac > yfracOffset)
		{
			float computed3 = _c - _d;
			float e2Term3 = _e2 - (_c + _d);
			return _d + computed3 * xfrac + e2Term3 * yfracOffset;
		}
		float e2Term4 = _e2 - (_d + _a);
		float computed4 = _d - _a;
		return _a + e2Term4 * xfrac + computed4 * yfrac;
	}

	public PointM PointA(int rbrow, int rbcol)
	{
		PointM result = default;
		result.X = _tile.CellMinX(rbcol);
		result.Y = _tile.CellMaxY(rbrow);
		result.Z = _a;
		result.M = _tile.CellSize;
		return result;
	}

	public PointM PointB(int rbrow, int rbcol)
	{
		PointM result = default;
		result.X = _tile.CellMaxX(rbcol);
		result.Y = _tile.CellMaxY(rbrow);
		result.Z = _b;
		result.M = _tile.CellSize;
		return result;
	}

	public PointM PointC(int rbrow, int rbcol)
	{
		PointM result = default;
		result.X = _tile.CellMaxX(rbcol);
		result.Y = _tile.CellMinY(rbrow);
		result.Z = _c;
		result.M = _tile.CellSize;
		return result;
	}

	public PointM PointD(int rbrow, int rbcol)
	{
		PointM result = default;
		result.X = _tile.CellMinX(rbcol);
		result.Y = _tile.CellMinY(rbrow);
		result.Z = _d;
		result.M = _tile.CellSize;
		return result;
	}

	public PointM PointE(int rbrow, int rbcol)
	{
		PointM result = default;
		result.X = _tile.CellCenterX(rbcol);
		result.Y = _tile.CellCenterY(rbrow);
		result.Z = (double)_e2 * 0.5;
		result.M = _tile.CellSize;
		return result;
	}
}
