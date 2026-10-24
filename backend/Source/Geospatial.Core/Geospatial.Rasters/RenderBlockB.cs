using System;
using Geospatial.Vectors;

namespace Geospatial.Rasters;

public struct RenderBlockB
{
	private RasterDefinition _sourceRaster;

	private ReadOnlyMemory<byte> _values;

	private int _maxRow;

	private int _maxCol;

	public int CurrentRow;

	public int CurrentCol;

	private int _n;

	public bool PriorReturnValue;

	private float _a;

	private float _b;

	private float _c;

	private float _d;

	private float _e2;

	private readonly TileWithNeighbors<byte> _mrRogers;

	private readonly float _noDataValue;

	public RenderBlockB(TileWithNeighbors<byte> neighborInfo)
		: this(neighborInfo.Center)
	{
		_mrRogers = neighborInfo;
	}

	public RenderBlockB(RasterData<byte> rd)
		: this(rd.RasterDefinition, rd.Data)
	{
	}

	public RenderBlockB(RasterDefinition tile, ReadOnlyMemory<byte> tileValues)
	{
		this = default;
		_noDataValue = 0f;
		SetTile(tile, tileValues);
	}

	public void SetTile(RasterDefinition sourceRaster, ReadOnlyMemory<byte> values)
	{
		_sourceRaster = sourceRaster;
		_values = values;
		_maxRow = sourceRaster.Rows - 1;
		_maxCol = sourceRaster.Cols - 1;
		CurrentRow = -1;
		CurrentCol = -1;
		_n = -1;
	}

	public RasterDefinition GetRenderBlockRasterDefinition()
	{
		double scaledCellSize = 0.5 * _sourceRaster.CellSize;
		Point upperLeftCorner = _sourceRaster.Extent.UpperLeft.Translate(scaledCellSize, 0.0 - scaledCellSize);
		if (_mrRogers != null)
		{
			return new RasterDefinition(upperLeftCorner, _sourceRaster.CellSize, _sourceRaster.Rows, _sourceRaster.Cols);
		}
		return new RasterDefinition(upperLeftCorner, _sourceRaster.CellSize, _sourceRaster.Rows - 1, _sourceRaster.Cols - 1);
	}

	public bool SetPixel(int rbRow, int rbCol)
	{
		if (rbRow == CurrentRow && rbCol == CurrentCol)
		{
			return PriorReturnValue;
		}
		CurrentRow = rbRow;
		CurrentCol = rbCol;
		_n = CurrentCol + CurrentRow * _sourceRaster.Cols;
		ReadOnlySpan<byte> byteSpan = _values.Span;
		_a = (int)byteSpan[_n];
		if (rbCol == _maxCol)
		{
			if (rbRow == _maxRow)
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = _noDataValue;
					}
					else
					{
						_b = (int)_mrRogers.Right.Data.Span[rbRow * _sourceRaster.Cols];
					}
					if (_mrRogers.LowerRight == null)
					{
						_c = _noDataValue;
					}
					else
					{
						_c = (int)_mrRogers.LowerRight.Data.Span[0];
					}
					if (_mrRogers.Bottom == null)
					{
						_d = _noDataValue;
					}
					else
					{
						_d = (int)_mrRogers.Bottom.Data.Span[_maxCol];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
					_d = _a;
				}
			}
			else
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = _noDataValue;
						_c = _noDataValue;
					}
					else
					{
						ReadOnlySpan<byte> byteSpan2 = _mrRogers.Right.Data.Span;
						_b = (int)byteSpan2[rbRow * _sourceRaster.Cols];
						_c = (int)byteSpan2[(rbRow + 1) * _sourceRaster.Cols];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
				}
				_d = (int)byteSpan[_n + _sourceRaster.Cols];
			}
		}
		else if (rbRow == _maxRow)
		{
			if (_mrRogers != null)
			{
				if (_mrRogers.Bottom == null)
				{
					_c = _noDataValue;
					_d = _noDataValue;
				}
				else
				{
					ReadOnlySpan<byte> byteSpan3 = _mrRogers.Bottom.Data.Span;
					_d = (int)byteSpan3[rbCol];
					_c = (int)byteSpan3[rbCol + 1];
				}
			}
			else
			{
				_c = _a;
				_d = _a;
			}
			_b = (int)byteSpan[_n + 1];
		}
		else
		{
			_b = (int)byteSpan[_n + 1];
			_c = (int)byteSpan[_n + _sourceRaster.Cols + 1];
			_d = (int)byteSpan[_n + _sourceRaster.Cols];
		}
		if (_a == _noDataValue || _b == _noDataValue || _c == _noDataValue || _d == _noDataValue)
		{
			PriorReturnValue = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * 0.5f;
		PriorReturnValue = true;
		return true;
	}

	public bool SetPixel_SkipPrior(int rbRow, int rbCol)
	{
		CurrentRow = rbRow;
		CurrentCol = rbCol;
		_n = CurrentCol + CurrentRow * _sourceRaster.Cols;
		if (_n < 0)
		{
			return false;
		}
		ReadOnlySpan<byte> byteSpan = _values.Span;
		_a = (int)byteSpan[_n];
		if (rbCol == _maxCol)
		{
			if (rbRow == _maxRow)
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = _noDataValue;
					}
					else
					{
						_b = (int)_mrRogers.Right.Data.Span[rbRow * _sourceRaster.Cols];
					}
					if (_mrRogers.LowerRight == null)
					{
						_c = _noDataValue;
					}
					else
					{
						_c = (int)_mrRogers.LowerRight.Data.Span[0];
					}
					if (_mrRogers.Bottom == null)
					{
						_d = _noDataValue;
					}
					else
					{
						_d = (int)_mrRogers.Bottom.Data.Span[_maxCol];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
					_d = _a;
				}
			}
			else
			{
				if (_mrRogers != null)
				{
					if (_mrRogers.Right == null)
					{
						_b = _noDataValue;
						_c = _noDataValue;
					}
					else
					{
						ReadOnlySpan<byte> byteSpan2 = _mrRogers.Right.Data.Span;
						_b = (int)byteSpan2[rbRow * _sourceRaster.Cols];
						_c = (int)byteSpan2[(rbRow + 1) * _sourceRaster.Cols];
					}
				}
				else
				{
					_b = _a;
					_c = _a;
				}
				_d = (int)byteSpan[_n + _sourceRaster.Cols];
			}
		}
		else if (rbRow == _maxRow)
		{
			if (_mrRogers != null)
			{
				if (_mrRogers.Bottom == null)
				{
					_c = _noDataValue;
					_d = _noDataValue;
				}
				else
				{
					ReadOnlySpan<byte> byteSpan3 = _mrRogers.Bottom.Data.Span;
					_d = (int)byteSpan3[rbCol];
					_c = (int)byteSpan3[rbCol + 1];
				}
			}
			else
			{
				_c = _a;
				_d = _a;
			}
			_b = (int)byteSpan[_n + 1];
		}
		else
		{
			_b = (int)byteSpan[_n + 1];
			_c = (int)byteSpan[_n + _sourceRaster.Cols + 1];
			_d = (int)byteSpan[_n + _sourceRaster.Cols];
		}
		if (_a == _noDataValue || _b == _noDataValue || _c == _noDataValue || _d == _noDataValue)
		{
			PriorReturnValue = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * 0.5f;
		PriorReturnValue = true;
		return true;
	}

	public bool SetPixel_NoPerimeterCheck(int rbRow, int rbCol)
	{
		if (rbRow == CurrentRow && rbCol == CurrentCol)
		{
			return PriorReturnValue;
		}
		CurrentRow = rbRow;
		CurrentCol = rbCol;
		_n = CurrentCol + CurrentRow * _sourceRaster.Cols;
		ReadOnlySpan<byte> byteSpan = _values.Span;
		_a = (int)byteSpan[_n];
		_b = (int)byteSpan[_n + 1];
		_c = (int)byteSpan[_n + _sourceRaster.Cols + 1];
		_d = (int)byteSpan[_n + _sourceRaster.Cols];
		if (_a == _noDataValue || _b == _noDataValue || _c == _noDataValue || _d == _noDataValue)
		{
			PriorReturnValue = false;
			return false;
		}
		_e2 = (_a + _b + _c + _d) * 0.5f;
		PriorReturnValue = true;
		return true;
	}

	public byte Interpolate(float xfrac, float yfrac)
	{
		float yfracOffset = 1f - yfrac;
		if (xfrac > yfrac)
		{
			if (xfrac < yfracOffset)
			{
				float computed = _b - _a;
				float e2Term = _e2 - (_a + _b);
				return (byte)(_a + computed * xfrac + e2Term * yfrac);
			}
			float e2Term2 = _e2 - (_b + _c);
			float computed2 = _c - _b;
			return (byte)(_b + e2Term2 * (1f - xfrac) + computed2 * yfrac);
		}
		if (xfrac > yfracOffset)
		{
			float computed3 = _c - _d;
			float e2Term3 = _e2 - (_c + _d);
			return (byte)(_d + computed3 * xfrac + e2Term3 * yfracOffset);
		}
		float e2Term4 = _e2 - (_d + _a);
		float computed4 = _d - _a;
		return (byte)(_a + e2Term4 * xfrac + computed4 * yfrac);
	}
}
