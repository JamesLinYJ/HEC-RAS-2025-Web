using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Utility.Core.Helpers;

namespace Geospatial;

public class TiledBitArray
{
	private readonly int _tileRows;

	private readonly int _tileCols;

	private readonly int _tileSize;

	private readonly int _cellsPerTile;

	private readonly Dictionary<int, BitArray> _masks;

	private readonly HashSet<int> _fullTiles;

	private readonly RasterDefinition _rasterDef;

	public int TileRows => _tileRows;

	public int TileCols => _tileCols;

	public int TileSize => _tileSize;

	public int FilledTileCount => _masks.Count;

	public IEnumerable<int> FilledTileIndices => _masks.Keys;

	public TiledBitArray(int tileRows, int tileCols, int tileSize, Dictionary<int, BitArray> masks)
		: this(tileRows, tileCols, tileSize, masks, CancellationToken.None)
	{
	}

	public TiledBitArray(int tileRows, int tileCols, int tileSize, Dictionary<int, BitArray> masks, CancellationToken cancellationToken)
	{
		_tileRows = tileRows;
		_tileCols = tileCols;
		_tileSize = tileSize;
		_cellsPerTile = tileSize * tileSize;
		_masks = masks;
		_fullTiles = new HashSet<int>();
		ComputeFullTileFlags(cancellationToken);
	}

	public TiledBitArray(int tileRows, int tileCols, int tileSize, Dictionary<int, BitArray> masks, RasterDefinition rasterDef)
		: this(tileRows, tileCols, tileSize, masks, rasterDef, CancellationToken.None)
	{
	}

	public TiledBitArray(int tileRows, int tileCols, int tileSize, Dictionary<int, BitArray> masks, RasterDefinition rasterDef, CancellationToken cancellationToken)
	{
		_tileRows = tileRows;
		_tileCols = tileCols;
		_tileSize = tileSize;
		_cellsPerTile = tileSize * tileSize;
		_rasterDef = rasterDef;
		_masks = masks;
		_fullTiles = new HashSet<int>();
		ComputeFullTileFlags(cancellationToken);
	}

	private void ComputeFullTileFlags(CancellationToken cancellationToken)
	{
		foreach (KeyValuePair<int, BitArray> mask in _masks)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (mask.Value.GetCardinality() == _cellsPerTile)
			{
				_fullTiles.Add(mask.Key);
			}
		}
	}

	public TileState GetTileState(int tileIdx)
	{
		if (_fullTiles.Contains(tileIdx))
		{
			return TileState.Full;
		}
		if (_masks.ContainsKey(tileIdx))
		{
			return TileState.Partial;
		}
		return TileState.Empty;
	}

	public BitArray GetTileMask(int tileIdx)
	{
		_masks.TryGetValue(tileIdx, out var tileMask);
		return tileMask;
	}

	public bool Get(int tileIdx, int localCellIdx)
	{
		if (_fullTiles.Contains(tileIdx))
		{
			return true;
		}
		if (_masks.TryGetValue(tileIdx, out var localValue))
		{
			return localValue[localCellIdx];
		}
		return false;
	}

	public bool AnyFilled()
	{
		return _masks.Count > 0;
	}

	public bool AnyFilledInTileRange(int minRow, int maxRow, int minCol, int maxCol)
	{
		for (int i = minRow; i <= maxRow; i++)
		{
			for (int j = minCol; j <= maxCol; j++)
			{
				int key = i * _tileCols + j;
				if (_masks.ContainsKey(key))
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool AllFilledInTileRange(int minRow, int maxRow, int minCol, int maxCol)
	{
		for (int i = minRow; i <= maxRow; i++)
		{
			for (int j = minCol; j <= maxCol; j++)
			{
				int tileColsTerm = i * _tileCols + j;
				if (!_fullTiles.Contains(tileColsTerm))
				{
					return false;
				}
			}
		}
		return true;
	}

	public bool AnyFilledInExtent(Extent extent)
	{
		if (!TryGetGlobalRange(extent, out var global))
		{
			return false;
		}
		GlobalRangeToTileRange(global, out var minTR, out var maxTR, out var minTC, out var maxTC);
		for (int i = minTR; i <= maxTR; i++)
		{
			for (int j = minTC; j <= maxTC; j++)
			{
				int tileColsTerm = i * _tileCols + j;
				if (_fullTiles.Contains(tileColsTerm))
				{
					return true;
				}
				if (_masks.TryGetValue(tileColsTerm, out var localValue) && AnySetInLocalRange(localValue, global, i, j))
				{
					return true;
				}
			}
		}
		return false;
	}

	public bool AllFilledInExtent(Extent extent)
	{
		if (!TryGetGlobalRange(extent, out var global))
		{
			return true;
		}
		GlobalRangeToTileRange(global, out var minTR, out var maxTR, out var minTC, out var maxTC);
		for (int i = minTR; i <= maxTR; i++)
		{
			for (int j = minTC; j <= maxTC; j++)
			{
				int tileColsTerm = i * _tileCols + j;
				if (!_fullTiles.Contains(tileColsTerm))
				{
					if (!_masks.TryGetValue(tileColsTerm, out var localValue))
					{
						return false;
					}
					if (!AllSetInLocalRange(localValue, global, i, j))
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	private bool TryGetGlobalRange(Extent extent, out PixelRange global)
	{
		global = _rasterDef.GetPixelRange(extent).ClipTo(_rasterDef);
		return !global.IsNull();
	}

	private void GlobalRangeToTileRange(PixelRange global, out int minTR, out int maxTR, out int minTC, out int maxTC)
	{
		minTR = Math.Max(0, global.MinRow / _tileSize);
		maxTR = Math.Min(_tileRows - 1, global.MaxRow / _tileSize);
		minTC = Math.Max(0, global.MinCol / _tileSize);
		maxTC = Math.Min(_tileCols - 1, global.MaxCol / _tileSize);
	}

	private bool AnySetInLocalRange(BitArray mask, PixelRange global, int tileRow, int tileCol)
	{
		LocalCellRange(global, tileRow, tileCol, out var localMinRow, out var localMaxRow, out var localMinCol, out var localMaxCol);
		int tileSize = _tileSize;
		for (int i = localMinRow; i <= localMaxRow; i++)
		{
			int tileSizeTerm = i * tileSize;
			for (int j = localMinCol; j <= localMaxCol; j++)
			{
				if (mask[tileSizeTerm + j])
				{
					return true;
				}
			}
		}
		return false;
	}

	private bool AllSetInLocalRange(BitArray mask, PixelRange global, int tileRow, int tileCol)
	{
		LocalCellRange(global, tileRow, tileCol, out var localMinRow, out var localMaxRow, out var localMinCol, out var localMaxCol);
		int tileSize = _tileSize;
		for (int i = localMinRow; i <= localMaxRow; i++)
		{
			int tileSizeTerm = i * tileSize;
			for (int j = localMinCol; j <= localMaxCol; j++)
			{
				if (!mask[tileSizeTerm + j])
				{
					return false;
				}
			}
		}
		return true;
	}

	private void LocalCellRange(PixelRange global, int tileRow, int tileCol, out int localMinRow, out int localMaxRow, out int localMinCol, out int localMaxCol)
	{
		int tileRowTimesTileSize = tileRow * _tileSize;
		int tileColTimesTileSize = tileCol * _tileSize;
		localMinRow = Math.Max(0, global.MinRow - tileRowTimesTileSize);
		localMaxRow = Math.Min(_tileSize - 1, global.MaxRow - tileRowTimesTileSize);
		localMinCol = Math.Max(0, global.MinCol - tileColTimesTileSize);
		localMaxCol = Math.Min(_tileSize - 1, global.MaxCol - tileColTimesTileSize);
	}
}
