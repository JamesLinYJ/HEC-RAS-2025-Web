using System.Collections.Generic;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public class StitchedRenderBlock
{
	public const byte NODATABYTE = 0;

	public const byte DATABYTE = 1;

	public const byte BOTTOM = 2;

	public const byte RIGHT = 4;

	public const byte LEFT = 8;

	public const byte TOP = 16;

	private RasterDefinition _tile;

	private float[] _tileValues;

	private float[] _perimeterValues;

	private byte[] _mask;

	private List<Segment3D> _stitches;

	private int _maxRow;

	private int _maxCol;

	private float _a;

	private float _b;

	private float _c;

	private float _d;

	private float _e2;

	public List<Segment3D> Stitches => _stitches;

	public void SetTile(RasterDefinition tile, float[] tileValues, float[] perimeterValues, byte[] mask, List<Segment3D> stitches)
	{
		_tile = tile;
		_tileValues = tileValues;
		_perimeterValues = perimeterValues;
		_mask = mask;
		_stitches = stitches;
		_maxRow = tile.Rows - 1;
		_maxCol = tile.Cols - 1;
	}

	public bool SetRowCol(int rbRow, int rbCol)
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
					if ((b & 8) != 0)
					{
						_stitches.Add(new Segment3D(PointD(rbRow, rbCol), PointA(rbRow, rbCol)));
					}
					if ((b & 0x10) != 0)
					{
						_stitches.Add(new Segment3D(PointA(rbRow, rbCol), PointB(rbRow, rbCol)));
					}
					if ((b & 4) != 0)
					{
						_stitches.Add(new Segment3D(PointB(rbRow, rbCol), PointC(rbRow, rbCol)));
					}
					if ((b & 2) != 0)
					{
						_stitches.Add(new Segment3D(PointC(rbRow, rbCol), PointD(rbRow, rbCol)));
					}
				}
			}
			return true;
		}
		return false;
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

	public Point3D PointA(int rbrow, int rbcol)
	{
		return new Point3D(_tile.CellMinX(rbcol), _tile.CellMaxY(rbrow), _a);
	}

	public Point3D PointB(int rbrow, int rbcol)
	{
		return new Point3D(_tile.CellMaxX(rbcol), _tile.CellMaxY(rbrow), _b);
	}

	public Point3D PointC(int rbrow, int rbcol)
	{
		return new Point3D(_tile.CellMaxX(rbcol), _tile.CellMinY(rbrow), _c);
	}

	public Point3D PointD(int rbrow, int rbcol)
	{
		return new Point3D(_tile.CellMinX(rbcol), _tile.CellMinY(rbrow), _d);
	}

	public Point3D PointE(int rbrow, int rbcol)
	{
		return new Point3D(_tile.CellCenterX(rbcol), _tile.CellCenterY(rbrow), (double)_e2 * 0.5);
	}
}
