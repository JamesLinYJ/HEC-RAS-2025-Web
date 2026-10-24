using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using H5Assist.Identifiers;
using TiffBinaryReader;
using Utility.Helpers;
using Utility.Progress;

namespace Geospatial.Terrain;

public class Raster : IDisposable
{
	internal struct Intersect(int start, int stop)
	{
		public int Start = start;

		public int Stop = stop;
	}

	private H5Writer _h5w;

	private TiffFile _ftr;

	private Extent _storedExtent;

	private string _tiffFileIn;

	private Extent[] _extentsWithDeadSpace;

	private Extent[] _extentsNoDeadSpace;

	private double[] _cellsize;

	private float[] _nodata;

	private int[] _imgHeight;

	private int[] _imgWidth;

	private int[] _numTiles;

	private int[] _tileWidth;

	private int[] _tileHeight;

	private int[] _numTilesWide;

	private int[] _numTilesTall;

	private List<Raster> _intersectingRasters;

	private int _numPLevels;

	public int RasterPriorityLevel;

	public const int TILE_STATS_ALL_NODATA = 0;

	public const int TILE_STATS_ALL_DATA = 1;

	public const int TILE_STATS_MIXED = 4;

	public const int TILE_STATS_ALL_INTERNAL_DATA = 129;

	public Raster(H5Writer Writer, string TiffFileIn, List<Raster> HigherPriorityRasters, int thisPriorityLevel)
	{
		_h5w = Writer;
		_tiffFileIn = TiffFileIn;
		RasterPriorityLevel = thisPriorityLevel;
		_ftr = new TiffFile(TiffFileIn);
		_numPLevels = _ftr.Directories.Count;
		_imgHeight = new int[_numPLevels];
		_imgWidth = new int[_numPLevels];
		_numTiles = new int[_numPLevels];
		_tileWidth = new int[_numPLevels];
		_tileHeight = new int[_numPLevels];
		_numTilesWide = new int[_numPLevels];
		_numTilesTall = new int[_numPLevels];
		_nodata = new float[_numPLevels];
		_cellsize = new double[_numPLevels];
		_extentsWithDeadSpace = new Extent[_numPLevels];
		_extentsNoDeadSpace = new Extent[_numPLevels];
		for (int i = 0; i < _numPLevels; i++)
		{
			_imgHeight[i] = (int)_ftr.Directories[i].Metadata.ImageHeight;
			_imgWidth[i] = (int)_ftr.Directories[i].Metadata.ImageWidth;
			_numTiles[i] = _ftr.Directories[i].Metadata.TileCount;
			_tileWidth[i] = _ftr.Directories[i].Metadata.TileWidth;
			_tileHeight[i] = _ftr.Directories[i].Metadata.TileHeight;
			_numTilesWide[i] = _ftr.Directories[i].Metadata.TilesWide;
			_numTilesTall[i] = _ftr.Directories[i].Metadata.TilesTall;
			_nodata[i] = ParseHelpers.TryParseStringAs_InvariantCulture<float>(_ftr.Directories[i].Metadata.NoData) ?? (-9999f);
			RasterDefinition rasterDefinition = RasterReader.TryReadRasterInfo(TiffFileIn);
			if (rasterDefinition.IsNull())
			{
				throw new Exception("Malformed Tiff Input? Couldn't read the extents.");
			}
			_storedExtent = rasterDefinition.Extent;
			_extentsNoDeadSpace[i] = _storedExtent;
			_cellsize[i] = (_extentsNoDeadSpace[i].UpperRight.X - _extentsNoDeadSpace[i].UpperLeft.X) / (double)_imgWidth[i];
			_extentsWithDeadSpace[i] = _extentsNoDeadSpace[i];
			_extentsWithDeadSpace[i] = _extentsWithDeadSpace[i].ExtendRight((double)(_tileWidth[i] * _numTilesWide[i] - _imgWidth[i]) * _cellsize[i]);
			_extentsWithDeadSpace[i] = _extentsWithDeadSpace[i].ExtendDown((double)(_tileHeight[i] * _numTilesTall[i] - _imgHeight[i]) * _cellsize[i]);
			if (_extentsWithDeadSpace[i].LowerLeft.Y > _extentsWithDeadSpace[i].UpperLeft.Y)
			{
				throw new Exception("This dataset needs to be georeferenced in such a way that LowerLeft.Y < UpperLeft.Y");
			}
		}
		_intersectingRasters = new List<Raster>();
		if (HigherPriorityRasters == null)
		{
			return;
		}
		foreach (Raster HigherPriorityRaster in HigherPriorityRasters)
		{
			Extent extentsNoDeadSpace = HigherPriorityRaster.GetExtentsNoDeadSpace(0);
			double cellSize = HigherPriorityRaster.GetCellSize(0);
			if (extentsNoDeadSpace.Extend(0.5 * cellSize).Overlaps(in _extentsNoDeadSpace[0]))
			{
				_intersectingRasters.Add(HigherPriorityRaster);
			}
		}
	}

	public void Dispose()
	{
		_ftr.Dispose();
	}

	public float[,] ReadStoredStats(int pLevel)
	{
		_h5w.ReadDataset(Terrain.GetMinMaxName(_tiffFileIn, pLevel), out float[,] values);
		return values;
	}

	public static float[] CombineTileWithPerim(float[] tile, float[,] perim, int tileH, int tileW)
	{
		float[] tileWithPerim = new float[tile.Length + perim.Length];
		CombineTileWithPerim(tile, perim, tileH, tileW, ref tileWithPerim);
		return tileWithPerim;
	}

	public static void CombineTileWithPerim(float[] tile, float[,] perim, int tileH, int tileW, ref float[] tileWithPerim)
	{
		if (tileWithPerim == null || tileWithPerim.Length != tile.Length + perim.Length)
		{
			tileWithPerim = new float[tile.Length + perim.Length];
		}
		for (int i = 0; i < tileH; i++)
		{
			Buffer.BlockCopy(tile, i * tileW * 4, tileWithPerim, i * (tileW + 1) * 4, tileW * 4);
			tileWithPerim[i * (tileW + 1) + tileW] = perim[0, i];
		}
		Buffer.BlockCopy(perim, 4 * tileH, tileWithPerim, 4 * tileH * (tileW + 1), (tileW + 1) * 4);
	}

	private void Block(byte[,] curMask, Extent curMaskExtents, Extent blockExtents, int maskCellsWide, int maskCellsTall, double curCellSize)
	{
		double lowerLeftBlockExtentsTerm = blockExtents.LowerLeft.X - curMaskExtents.LowerLeft.X;
		double lowerRightBlockExtentsTerm = blockExtents.LowerRight.X - curMaskExtents.LowerLeft.X;
		double upperLeftCurMaskExtentsTerm = curMaskExtents.UpperLeft.Y - blockExtents.UpperLeft.Y;
		double upperLeftCurMaskExtentsTerm2 = curMaskExtents.UpperLeft.Y - blockExtents.LowerLeft.Y;
		int curCellSizeTerm = (int)Math.Floor(lowerLeftBlockExtentsTerm / curCellSize);
		int count = (int)Math.Floor(lowerRightBlockExtentsTerm / curCellSize);
		int curCellSizeTerm2 = (int)Math.Floor(upperLeftCurMaskExtentsTerm / curCellSize);
		int count2 = (int)Math.Floor(upperLeftCurMaskExtentsTerm2 / curCellSize);
		for (int i = curCellSizeTerm2; i <= count2; i++)
		{
			for (int j = curCellSizeTerm; j <= count; j++)
			{
				if (i >= 0 && i < maskCellsTall && j >= 0 && j < maskCellsWide)
				{
					int maskCellsWideTerm = i * maskCellsWide + j;
					curMask[0, maskCellsWideTerm] = 0;
				}
			}
		}
	}

	private byte[,] CreateNaiiveRenderBlockMask(int TileNumber, int PyramidLevel)
	{
		string perimeterName = Terrain.GetPerimeterName(_tiffFileIn, PyramidLevel);
		float noData = GetNoData(PyramidLevel);
		int tileWidth = GetTileWidth(PyramidLevel);
		int tileHeight = GetTileHeight(PyramidLevel);
		int numTilesWide = GetNumTilesWide(PyramidLevel);
		GetNumTilesTall(PyramidLevel);
		int numCols = GetNumCols(PyramidLevel);
		int numRows = GetNumRows(PyramidLevel);
		int tileNumberWithNumTilesWide = TileNumber % numTilesWide;
		int tileNumberOverNumTilesWide = TileNumber / numTilesWide;
		float[] floatValues = new float[tileWidth * tileHeight];
		float[,] values = new float[1, tileWidth + tileHeight + 1];
		_ftr.Directories[PyramidLevel].GetTile<float>(TileNumber, floatValues);
		int tileHeightTerm = tileHeight * (tileNumberOverNumTilesWide + 1) - 1;
		if (tileWidth * (tileNumberWithNumTilesWide + 1) - 1 > numCols || tileHeightTerm > numRows)
		{
			int tileHeightTerm2 = tileHeight * tileNumberOverNumTilesWide;
			int tileWidthTerm = tileWidth * tileNumberWithNumTilesWide;
			int numColsTerm = numCols - tileWidthTerm - 1;
			int numRowsTerm = numRows - tileHeightTerm2 - 1;
			for (int i = 0; i < tileHeight; i++)
			{
				for (int j = 0; j < tileWidth; j++)
				{
					if (i > numRowsTerm || j > numColsTerm)
					{
						floatValues[i * tileWidth + j] = noData;
					}
				}
			}
		}
		_h5w.ReadRows(perimeterName, TileNumber, 1, ref values);
		float[] combineTileWithPerim = CombineTileWithPerim(floatValues, values, tileHeight, tileWidth);
		byte[,] naiiveRenderBlockMask = new byte[1, tileHeight * tileWidth];
		for (int k = 0; k < tileHeight; k++)
		{
			for (int l = 0; l < tileWidth; l++)
			{
				int tileWidthTerm2 = k * tileWidth + l;
				int tileWidthTerm3 = k * (tileWidth + 1) + l;
				int ordinal = tileWidthTerm3 + 1;
				int tileWidthTerm4 = tileWidthTerm3 + (tileWidth + 1);
				int tileWidthTerm5 = ordinal + (tileWidth + 1);
				if (combineTileWithPerim[tileWidthTerm3] == noData || combineTileWithPerim[ordinal] == noData || combineTileWithPerim[tileWidthTerm4] == noData || combineTileWithPerim[tileWidthTerm5] == noData)
				{
					naiiveRenderBlockMask[0, tileWidthTerm2] = 0;
				}
				else
				{
					naiiveRenderBlockMask[0, tileWidthTerm2] = 1;
				}
			}
		}
		return naiiveRenderBlockMask;
	}

	public void ConvertOnOffMaskToPerimeterMask(Action ReportTile, Func<bool> EarlyExitCheck)
	{
		for (int i = 0; i < _numPLevels; i++)
		{
			if (EarlyExitCheck())
			{
				break;
			}
			string maskName = Terrain.GetMaskName(_tiffFileIn, i);
			int tilesWide = GetNumTilesWide(i);
			int numTilesTall = GetNumTilesTall(i);
			int tileWidth = GetTileWidth(i);
			int tileHeight = GetTileHeight(i);
			int numRows = GetNumRows(i);
			int numCols = GetNumCols(i);
			byte[][,] tiles = new byte[tilesWide * numTilesTall][,];
			byte[,] tiles2 = new byte[1, tileWidth * tileHeight];
			for (int j = 0; j < tileWidth * tileHeight; j++)
			{
				tiles2[0, j] = 0;
			}
			int intervalIndex = 0;
			int ordinal = 0;
			int numTilesTall2 = Math.Min(numTilesTall, 20);
			int tilesWide2 = Math.Min(tilesWide, 2);
			_h5w.ReadDataset(Terrain.GetMinMaxName(_tiffFileIn, i), out float[,] values);
			bool[,] boolValues = new bool[numTilesTall, tilesWide];
			for (int k = 0; k < numTilesTall; k++)
			{
				for (int l = 0; l < tilesWide; l++)
				{
					if (values[k * tilesWide + l, 2] == 1f)
					{
						boolValues[k, l] = false;
					}
					else
					{
						boolValues[k, l] = true;
					}
				}
			}
			while (true)
			{
				if (tilesWide2 != 0)
				{
					if (EarlyExitCheck())
					{
						return;
					}
					for (int m = ordinal; m < ordinal + numTilesTall2; m++)
					{
						for (int n = intervalIndex; n < intervalIndex + tilesWide2; n++)
						{
							int tilesWideTerm = m * tilesWide + n;
							if (tiles[tilesWideTerm] == null)
							{
								if (!boolValues[m, n])
								{
									tiles[tilesWideTerm] = tiles2;
									continue;
								}
								byte[,] values2 = new byte[0, 0];
								_h5w.ReadRows(maskName, tilesWideTerm, 1, ref values2);
								tiles[tilesWideTerm] = values2;
							}
						}
					}
					int tileHeightTerm = ordinal * tileHeight;
					int tileHeightTerm2 = (ordinal + numTilesTall2) * tileHeight;
					int tileWidthTerm = intervalIndex * tileWidth;
					int tileWidthTerm2 = (intervalIndex + tilesWide2) * tileWidth;
					for (int ordinal2 = ordinal; ordinal2 < ordinal + numTilesTall2; ordinal2++)
					{
						for (int ordinal3 = intervalIndex; ordinal3 < intervalIndex + tilesWide2; ordinal3++)
						{
							if (!boolValues[ordinal2, ordinal3])
							{
								continue;
							}
							int tileHeightTerm3 = ordinal2 * tileHeight;
							int count = (ordinal2 + 1) * tileHeight;
							int tileWidthTerm3 = ordinal3 * tileWidth;
							if (ordinal3 == intervalIndex && intervalIndex != 0)
							{
								tileWidthTerm3 += 255;
							}
							int count2 = (ordinal3 + 1) * tileWidth;
							for (int ordinal4 = tileHeightTerm3; ordinal4 < count; ordinal4++)
							{
								for (int ordinal5 = tileWidthTerm3; ordinal5 < count2; ordinal5++)
								{
									int tileWidthTerm4 = ordinal5 / tileWidth;
									int tileHeightTerm4 = ordinal4 / tileHeight;
									int tilesWideTerm2 = tileHeightTerm4 * tilesWide + tileWidthTerm4;
									int tileWidthTerm5 = tileWidthTerm4 * tileWidth;
									int tileHeightTerm5 = tileHeightTerm4 * tileHeight;
									int tileWidthTerm6 = (ordinal4 - tileHeightTerm5) * tileWidth + (ordinal5 - tileWidthTerm5);
									byte b = tiles[tilesWideTerm2][0, tileWidthTerm6];
									if (b == 0)
									{
										continue;
									}
									bool condition = ordinal4 == tileHeightTerm;
									bool condition2 = ordinal5 == tileWidthTerm;
									bool condition3 = ordinal5 == tileWidthTerm2 - 1;
									bool condition4 = ordinal4 == tileHeightTerm2 - 1;
									if (!condition)
									{
										int ordinal6 = ordinal5;
										int ordinal7 = ordinal4 - 1;
										int tileWidthTerm7 = ordinal6 / tileWidth;
										int tileHeightTerm6 = ordinal7 / tileHeight;
										int tilesWideTerm3 = tileHeightTerm6 * tilesWide + tileWidthTerm7;
										int tileWidthTerm8 = tileWidthTerm7 * tileWidth;
										int tileHeightTerm7 = tileHeightTerm6 * tileHeight;
										int tileWidthTerm9 = (ordinal7 - tileHeightTerm7) * tileWidth + (ordinal6 - tileWidthTerm8);
										if (tiles[tilesWideTerm3][0, tileWidthTerm9] == 0)
										{
											b |= 0x10;
										}
									}
									if (!condition2)
									{
										int ordinal8 = ordinal5 - 1;
										int ordinal9 = ordinal4;
										int tileWidthTerm10 = ordinal8 / tileWidth;
										int tileHeightTerm8 = ordinal9 / tileHeight;
										int tilesWideTerm4 = tileHeightTerm8 * tilesWide + tileWidthTerm10;
										int tileWidthTerm11 = tileWidthTerm10 * tileWidth;
										int tileHeightTerm9 = tileHeightTerm8 * tileHeight;
										int tileWidthTerm12 = (ordinal9 - tileHeightTerm9) * tileWidth + (ordinal8 - tileWidthTerm11);
										if (tiles[tilesWideTerm4][0, tileWidthTerm12] == 0)
										{
											b |= 8;
										}
									}
									if (!condition3)
									{
										int ordinal10 = ordinal5 + 1;
										int ordinal11 = ordinal4;
										int tileWidthTerm13 = ordinal10 / tileWidth;
										int tileHeightTerm10 = ordinal11 / tileHeight;
										int tilesWideTerm5 = tileHeightTerm10 * tilesWide + tileWidthTerm13;
										int tileWidthTerm14 = tileWidthTerm13 * tileWidth;
										int tileHeightTerm11 = tileHeightTerm10 * tileHeight;
										int tileWidthTerm15 = (ordinal11 - tileHeightTerm11) * tileWidth + (ordinal10 - tileWidthTerm14);
										if (tiles[tilesWideTerm5][0, tileWidthTerm15] == 0)
										{
											b |= 4;
										}
									}
									if (!condition4)
									{
										int ordinal12 = ordinal5;
										int ordinal13 = ordinal4 + 1;
										int tileWidthTerm16 = ordinal12 / tileWidth;
										int tileHeightTerm12 = ordinal13 / tileHeight;
										int tilesWideTerm6 = tileHeightTerm12 * tilesWide + tileWidthTerm16;
										int tileWidthTerm17 = tileWidthTerm16 * tileWidth;
										int tileHeightTerm13 = tileHeightTerm12 * tileHeight;
										int tileWidthTerm18 = (ordinal13 - tileHeightTerm13) * tileWidth + (ordinal12 - tileWidthTerm17);
										if (tiles[tilesWideTerm6][0, tileWidthTerm18] == 0)
										{
											b |= 2;
										}
									}
									bool condition5 = ordinal4 == 0;
									bool condition6 = ordinal5 == 0;
									bool numColsTerm = ordinal5 == numCols;
									bool numRowsTerm = ordinal4 == numRows;
									if (condition5)
									{
										b |= 0x10;
									}
									if (condition6)
									{
										b |= 8;
									}
									if (numColsTerm)
									{
										b |= 4;
									}
									if (numRowsTerm)
									{
										b |= 2;
									}
									int tileWidthTerm19 = ordinal5 / tileWidth;
									int tileHeightTerm14 = ordinal4 / tileHeight;
									int tilesWideTerm7 = tileHeightTerm14 * tilesWide + tileWidthTerm19;
									int tileWidthTerm20 = tileWidthTerm19 * tileWidth;
									int tileHeightTerm15 = tileHeightTerm14 * tileHeight;
									int tileWidthTerm21 = (ordinal4 - tileHeightTerm15) * tileWidth + (ordinal5 - tileWidthTerm20);
									if (ordinal4 > numRows || (ordinal5 > numCols && b != 0))
									{
										b = 0;
									}
									tiles[tilesWideTerm7][0, tileWidthTerm21] = b;
								}
							}
						}
					}
					for (int ordinal14 = ordinal; ordinal14 < ordinal + numTilesTall2; ordinal14++)
					{
						int tilesWideTerm8 = ordinal14 * tilesWide + intervalIndex;
						if (boolValues[ordinal14, intervalIndex])
						{
							_h5w.AddRows(maskName, tiles[tilesWideTerm8], 1, tilesWideTerm8);
						}
						tiles[tilesWideTerm8] = null;
						ReportTile();
					}
					intervalIndex++;
					tilesWide2 = Math.Min(tilesWide - intervalIndex, 2);
				}
				else
				{
					intervalIndex = 0;
					tilesWide2 = Math.Min(tilesWide, 2);
					ordinal += numTilesTall2 - 1;
					numTilesTall2 = Math.Min(numTilesTall - ordinal, 20);
					if (numTilesTall2 == 1)
					{
						break;
					}
				}
			}
		}
	}

	public void ConvertOnOffMaskToPerimeterMaskParallel(byte[][] pyramidTileStats, Action<int> ReportTiles, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		object h5lock = new object();
		byte[] allDataTileBuf = new byte[65536];
		for (int i = 0; i < allDataTileBuf.Length; i++)
		{
			allDataTileBuf[i] = 1;
		}
		for (int j = 0; j < _numPLevels; j++)
		{
			if (reporter.CancelRequested)
			{
				break;
			}
			string maskName = Terrain.GetMaskName(_tiffFileIn, j);
			H5DatasetID DATASET = _h5w.OpenDataset(maskName);
			try
			{
				int tilesWide = GetNumTilesWide(j);
				int tilesTall = GetNumTilesTall(j);
				int tileCount = tilesTall * tilesWide;
				int tileWidth = GetTileWidth(j);
				int tileHeight = GetTileHeight(j);
				int cellsPerTile = tileWidth * tileHeight;
				byte[] tileStats = pyramidTileStats[j];
				bool[] tileChanged = new bool[tileCount];
				byte[][] tileArrays = new byte[tileCount][];
				long[] chunkSize = _h5w.GetChunkSize(maskName);
				int rowsPerChunk = (int)chunkSize[0];
				int chunkCount = (int)Math.Ceiling((double)tileCount / (double)rowsPerChunk);
				bool[] tileComplete = new bool[tileCount];
				bool[] chunksComplete = new bool[chunkCount];
				bool[] chunksLoaded = new bool[chunkCount];
				object[] chunkLocks = new object[chunkCount];
				Func<int, int> ChunkIndexFromTileIndex = (int tileIdx) => tileIdx / rowsPerChunk;
				Func<int, int> GetLocalRowCountFromChunk = (int ordinal) =>
				{
					int rowsPerChunk2 = rowsPerChunk;
					if (ordinal == chunkCount - 1)
					{
						rowsPerChunk2 -= chunkCount * rowsPerChunk - tileCount;
					}
					return rowsPerChunk2;
				};
				Func<int, byte, bool> IsEntireChunkValue = (int ordinal2, byte localValue) =>
				{
					int rowsPerChunkTerm = ordinal2 * rowsPerChunk;
					int localRowCountFromChunk = GetLocalRowCountFromChunk(ordinal2);
					for (int k = 0; k < localRowCountFromChunk; k++)
					{
						int ordinal3 = rowsPerChunkTerm + k;
						if ((tileStats[ordinal3] | localValue) != localValue)
						{
							return false;
						}
					}
					return true;
				};
				int[][] chunkDependencies = new int[chunkCount][];
				int chunkIdx;
				for (chunkIdx = 0; chunkIdx < chunkCount; chunkIdx++)
				{
					chunkLocks[chunkIdx] = new object();
					int localRowCountFromChunk2 = GetLocalRowCountFromChunk(chunkIdx);
					HashSet<int> intSet = new HashSet<int>();
					for (int ordinal4 = 0; ordinal4 < localRowCountFromChunk2; ordinal4++)
					{
						int chunkIdxRowsPerChunkTerm = chunkIdx * rowsPerChunk + ordinal4;
						int tilesWideTerm = chunkIdxRowsPerChunkTerm % tilesWide;
						int tilesWideTerm2 = chunkIdxRowsPerChunkTerm / tilesWide;
						bool condition = tilesWideTerm2 == 0;
						bool tilesTallTerm = tilesWideTerm2 == tilesTall - 1;
						bool condition2 = tilesWideTerm == 0;
						bool tilesWideTerm3 = tilesWideTerm == tilesWide - 1;
						if (!condition2)
						{
							intSet.Add(chunkIdxRowsPerChunkTerm - 1);
						}
						if (!tilesWideTerm3)
						{
							intSet.Add(chunkIdxRowsPerChunkTerm + 1);
						}
						if (!condition)
						{
							intSet.Add(chunkIdxRowsPerChunkTerm - tilesWide);
						}
						if (!tilesTallTerm)
						{
							intSet.Add(chunkIdxRowsPerChunkTerm + tilesWide);
						}
					}
					chunkDependencies[chunkIdx] = (from tIdx in intSet.Select((int tIdx) => ChunkIndexFromTileIndex(tIdx)).Distinct()
						where tIdx != chunkIdx
						select tIdx).ToArray();
					if (IsEntireChunkValue(chunkIdx, 0))
					{
						chunksComplete[chunkIdx] = true;
					}
				}
				Action<int> UnloadChunkIfPossible = null;
				UnloadChunkIfPossible = (int ordinal5) =>
				{
					if (chunksLoaded[ordinal5])
					{
						int[] chunkDependency;
						lock (chunkLocks[ordinal5])
						{
							if (!chunksLoaded[ordinal5] || !chunksComplete[ordinal5])
							{
								return;
							}
							chunkDependency = chunkDependencies[ordinal5];
							foreach (int chunkDependency2 in chunkDependency)
							{
								if (!chunksComplete[chunkDependency2])
								{
									return;
								}
							}
							chunksLoaded[ordinal5] = false;
							int localRowCountFromChunk3 = GetLocalRowCountFromChunk(ordinal5);
							bool condition3 = false;
							for (int l = 0; l < localRowCountFromChunk3; l++)
							{
								int rowsPerChunkTerm2 = ordinal5 * rowsPerChunk + l;
								if (tileChanged[rowsPerChunkTerm2])
								{
									condition3 = true;
								}
							}
							if (condition3)
							{
								byte[,] byteValues = new byte[localRowCountFromChunk3, tileWidth * tileHeight];
								for (int m = 0; m < localRowCountFromChunk3; m++)
								{
									int rowsPerChunkTerm3 = ordinal5 * rowsPerChunk + m;
									if (tileStats[rowsPerChunkTerm3] != 0)
									{
										byte[] tileArray = tileArrays[rowsPerChunkTerm3];
										Buffer.BlockCopy(tileArray, 0, byteValues, m * tileArray.Length, tileArray.Length);
									}
								}
								lock (h5lock)
								{
									_h5w.AddRows(DATASET, byteValues, localRowCountFromChunk3, ordinal5 * rowsPerChunk);
								}
							}
							for (int n = 0; n < localRowCountFromChunk3; n++)
							{
								int rowsPerChunkTerm4 = ordinal5 * rowsPerChunk + n;
								tileArrays[rowsPerChunkTerm4] = null;
							}
						}
						chunkDependency = chunkDependencies[ordinal5];
						foreach (int chunkDependency3 in chunkDependency)
						{
							UnloadChunkIfPossible(chunkDependency3);
						}
					}
				};
				Action<int, bool> FinishedTile = (int tileIdx, bool changesMade) =>
				{
					tileComplete[tileIdx] = true;
					tileChanged[tileIdx] = changesMade;
					int chunkIndexFromTileIndex = ChunkIndexFromTileIndex(tileIdx);
					int localRowCountFromChunk4 = GetLocalRowCountFromChunk(chunkIndexFromTileIndex);
					for (int k = 0; k < localRowCountFromChunk4; k++)
					{
						int chunkIndexFromTileIndexRowsPerChunkTerm = chunkIndexFromTileIndex * rowsPerChunk + k;
						if (!tileComplete[chunkIndexFromTileIndexRowsPerChunkTerm])
						{
							return;
						}
					}
					chunksComplete[chunkIndexFromTileIndex] = true;
					UnloadChunkIfPossible(chunkIndexFromTileIndex);
				};
				Action<int> LoadChunkIfNecessary = (int ordinal6) =>
				{
					if (IsEntireChunkValue(ordinal6, 0))
					{
						return;
					}
					lock (chunkLocks[ordinal6])
					{
						if (!chunksLoaded[ordinal6])
						{
							int rowsPerChunkTerm5 = ordinal6 * rowsPerChunk;
							int localRowCountFromChunk5 = GetLocalRowCountFromChunk(ordinal6);
							if (IsEntireChunkValue(ordinal6, 1))
							{
								for (int k = 0; k < localRowCountFromChunk5; k++)
								{
									int ordinal7 = rowsPerChunkTerm5 + k;
									tileArrays[ordinal7] = new byte[cellsPerTile];
									Buffer.BlockCopy(allDataTileBuf, 0, tileArrays[ordinal7], 0, cellsPerTile);
								}
							}
							else
							{
								byte[,] values = new byte[localRowCountFromChunk5, cellsPerTile];
								lock (h5lock)
								{
									_h5w.ReadRows(DATASET, rowsPerChunkTerm5, localRowCountFromChunk5, ref values);
								}
								for (int l = 0; l < localRowCountFromChunk5; l++)
								{
									int ordinal8 = rowsPerChunkTerm5 + l;
									if (tileStats[ordinal8] != 0)
									{
										byte[] tileArraysArray = new byte[cellsPerTile];
										Buffer.BlockCopy(values, l * cellsPerTile, tileArraysArray, 0, cellsPerTile);
										tileArrays[ordinal8] = tileArraysArray;
									}
								}
							}
							chunksLoaded[ordinal6] = true;
						}
					}
				};
				Action<int> PrepareChunkAndNeighbors = (int ordinal9) =>
				{
					LoadChunkIfNecessary(ordinal9);
					int[] chunkDependencyArray = chunkDependencies[ordinal9];
					foreach (int chunkDependency4 in chunkDependencyArray)
					{
						LoadChunkIfNecessary(chunkDependency4);
					}
				};
				Action<int> EnsurePrepareTile = (int tileIdx) =>
				{
					PrepareChunkAndNeighbors(ChunkIndexFromTileIndex(tileIdx));
				};
				Action<int> OperateOnTile = (int tileIdx) =>
				{
					if (tileStats[tileIdx] == 0)
					{
						FinishedTile(tileIdx, arg2: false);
					}
					else
					{
						int tileIdxWithTilesWide = tileIdx % tilesWide;
						int tileIdxOverTilesWide = tileIdx / tilesWide;
						bool tileIdxWithTilesWideIsZero = tileIdxWithTilesWide == 0;
						bool tilesWideTerm4 = tileIdxWithTilesWide == tilesWide - 1;
						bool tileIdxOverTilesWideIsZero = tileIdxOverTilesWide == 0;
						bool tilesTallTerm2 = tileIdxOverTilesWide == tilesTall - 1;
						if (tileStats[tileIdx] == 1 && !tileIdxWithTilesWideIsZero && !tilesWideTerm4 && !tileIdxOverTilesWideIsZero && !tilesTallTerm2)
						{
							byte b = tileStats[tileIdx - 1];
							byte b2 = tileStats[tileIdx + 1];
							byte b3 = tileStats[tileIdx - tilesWide];
							byte b4 = tileStats[tileIdx + tilesWide];
							if ((b | 1) == 1 && (b2 | 1) == 1 && (b3 | 1) == 1 && (b4 | 1) == 1)
							{
								tileStats[tileIdx] = 129;
								FinishedTile(tileIdx, arg2: false);
								return;
							}
						}
						EnsurePrepareTile(tileIdx);
						byte[] tileArrayArray = tileArrays[tileIdx];
						byte[] tileArray2 = null;
						byte[] tileArray3 = null;
						byte[] tileArray4 = null;
						byte[] tileArray5 = null;
						if (!tileIdxWithTilesWideIsZero)
						{
							tileArray2 = tileArrays[tileIdx - 1];
						}
						if (!tilesWideTerm4)
						{
							tileArray3 = tileArrays[tileIdx + 1];
						}
						if (!tileIdxOverTilesWideIsZero)
						{
							tileArray4 = tileArrays[tileIdx - tilesWide];
						}
						if (!tilesTallTerm2)
						{
							tileArray5 = tileArrays[tileIdx + tilesWide];
						}
						bool tileStatIsNonZero = !tileIdxWithTilesWideIsZero && tileStats[tileIdx - 1] != 0;
						bool tileStatIsNonZero2 = !tilesWideTerm4 && tileStats[tileIdx + 1] != 0;
						bool tileStatIsNonZero3 = !tileIdxOverTilesWideIsZero && tileStats[tileIdx - tilesWide] != 0;
						bool tileStatIsNonZero4 = !tilesTallTerm2 && tileStats[tileIdx + tilesWide] != 0;
						bool condition4 = false;
						for (int k = 0; k < tileHeight; k++)
						{
							bool condition5 = k == 0;
							bool tileHeightTerm = k == tileHeight - 1;
							for (int l = 0; l < tileWidth; l++)
							{
								int tileWidthTerm = k * tileWidth + l;
								byte b5 = tileArrayArray[tileWidthTerm];
								if (b5 != 0)
								{
									bool condition6 = l == 0;
									bool tileWidthTerm2 = l == tileWidth - 1;
									if (((!condition5) ? tileArrayArray[tileWidthTerm - tileWidth] : ((!tileIdxOverTilesWideIsZero && tileStatIsNonZero3 && tileArray4[tileWidth * (tileHeight - 1) + l] != 0) ? ((byte)1) : ((byte)0))) == 0)
									{
										condition4 = true;
										b5 |= 0x10;
									}
									if (((!condition6) ? tileArrayArray[tileWidthTerm - 1] : ((!tileIdxWithTilesWideIsZero && tileStatIsNonZero && tileArray2[tileWidth * k + (tileWidth - 1)] != 0) ? ((byte)1) : ((byte)0))) == 0)
									{
										condition4 = true;
										b5 |= 8;
									}
									if (((!tileWidthTerm2) ? tileArrayArray[tileWidthTerm + 1] : ((!tilesWideTerm4 && tileStatIsNonZero2 && tileArray3[tileWidth * k] != 0) ? ((byte)1) : ((byte)0))) == 0)
									{
										condition4 = true;
										b5 |= 4;
									}
									if (((!tileHeightTerm) ? tileArrayArray[tileWidthTerm + tileWidth] : ((!tilesTallTerm2 && tileStatIsNonZero4 && tileArray5[l] != 0) ? ((byte)1) : ((byte)0))) == 0)
									{
										condition4 = true;
										b5 |= 2;
									}
									tileArrayArray[tileWidthTerm] = b5;
								}
							}
						}
						if (condition4)
						{
							FinishedTile(tileIdx, arg2: true);
						}
						else
						{
							tileStats[tileIdx] = 129;
							FinishedTile(tileIdx, arg2: false);
						}
					}
				};
				ParallelOptions parallelOptions = new ParallelOptions();
				parallelOptions.MaxDegreeOfParallelism = 16;
				int activeRowCounter = 0;
				Parallel.For(0, tilesTall, parallelOptions, (int _) =>
				{
					int incrementInterlockedTerm = Interlocked.Increment(ref activeRowCounter) - 1;
					for (int k = 0; k < tilesWide; k++)
					{
						OperateOnTile(incrementInterlockedTerm * tilesWide + k);
					}
					ReportTiles(tilesWide);
				});
			}
			finally
			{
				if (DATASET != null)
				{
					((IDisposable)DATASET).Dispose();
				}
			}
		}
	}

	public void CreatePrioritizedRenderblockOnOffMask(int pyramidLevel, byte[][][] inOutRenderBlockTileStats, Action<int> ReportTilesDone, ProgressReporter reporter)
	{
		object h5lock = new object();
		GetNoData(pyramidLevel);
		GetNumCols(pyramidLevel);
		GetNumRows(pyramidLevel);
		int tileWidth = GetTileWidth(pyramidLevel);
		int tileHeight = GetTileHeight(pyramidLevel);
		int tilesWide = GetNumTilesWide(pyramidLevel);
		int numTilesTall = GetNumTilesTall(pyramidLevel);
		int tileCount = tilesWide * numTilesTall;
		double curCellSize = GetCellSize(pyramidLevel);
		if (inOutRenderBlockTileStats[RasterPriorityLevel] == null)
		{
			inOutRenderBlockTileStats[RasterPriorityLevel] = new byte[_numPLevels][];
		}
		inOutRenderBlockTileStats[RasterPriorityLevel][pyramidLevel] = new byte[tileCount];
		Terrain.GetPerimeterName(_tiffFileIn, pyramidLevel);
		string maskName = Terrain.GetMaskName(_tiffFileIn, pyramidLevel);
		_h5w.Delete(maskName);
		int rowsPerChunk = Math.Min(tilesWide * numTilesTall, 4194304 / (tileWidth * tileHeight));
		int numChunks = (int)Math.Ceiling((float)tileCount / (float)rowsPerChunk);
		_h5w.Create2dExtendibleDataset(maskName, rowsPerChunk, tileWidth * tileHeight, (byte)0);
		H5DatasetID EXPERIMENTAL_WRITER = _h5w.OpenDataset(maskName, rowsPerChunk * tileWidth * tileHeight * 16);
		ThreadLocal<byte[,]> tlGetByteMaskBuffer = new ThreadLocal<byte[,]>(() => new byte[1, tileWidth * tileHeight]);
		int[] pLevelsHigher = new int[_intersectingRasters.Count];
		float[][,] floatValues = new float[_intersectingRasters.Count][,];
		Extent[][] higherRenderBlockTileCornersPerRaster = new Extent[_intersectingRasters.Count][];
		Extent[][] higherRenderBlockTileCornersExtendedPerRaster = new Extent[_intersectingRasters.Count][];
		H5DatasetID[] EXPERIMENTAL_READERS = new H5DatasetID[_intersectingRasters.Count];
		for (int intervalIndex = 0; intervalIndex < _intersectingRasters.Count; intervalIndex++)
		{
			Raster raster = _intersectingRasters[intervalIndex];
			int pLevelsHigher2 = 0;
			if (pyramidLevel == 0)
			{
				pLevelsHigher2 = 0;
			}
			else
			{
				for (int pLevelsHigher22 = 0; pLevelsHigher22 < raster._numPLevels && !(curCellSize <= raster.GetCellSize(pLevelsHigher22)); pLevelsHigher22++)
				{
					pLevelsHigher2 = pLevelsHigher22;
				}
			}
			pLevelsHigher[intervalIndex] = pLevelsHigher2;
			floatValues[intervalIndex] = raster.ReadStoredStats(pLevelsHigher2);
			double cellSize = raster.GetCellSize(pLevelsHigher2);
			int numTiles = raster.GetNumTiles(pLevelsHigher2);
			raster.GetTileWidth(pLevelsHigher2);
			raster.GetTileHeight(pLevelsHigher2);
			int numTilesWide = raster.GetNumTilesWide(pLevelsHigher2);
			raster.GetNumTilesTall(pLevelsHigher2);
			higherRenderBlockTileCornersPerRaster[intervalIndex] = new Extent[numTiles];
			higherRenderBlockTileCornersExtendedPerRaster[intervalIndex] = new Extent[numTiles];
			string maskName2 = raster.GetMaskName(pLevelsHigher2);
			EXPERIMENTAL_READERS[intervalIndex] = _h5w.OpenDataset(maskName2);
			for (int intervalIndex2 = 0; intervalIndex2 < numTiles; intervalIndex2++)
			{
				int tileNumX = intervalIndex2 % numTilesWide;
				int tileNumY = intervalIndex2 / numTilesWide;
				Extent extent = raster.GetTileExtents(pLevelsHigher2, tileNumX, tileNumY).MoveDown(0.5 * cellSize).MoveRight(0.5 * cellSize);
				higherRenderBlockTileCornersPerRaster[intervalIndex][intervalIndex2] = extent;
				Extent extent2 = extent.Extend(0.25 * cellSize);
				higherRenderBlockTileCornersExtendedPerRaster[intervalIndex][intervalIndex2] = extent2;
			}
		}
		try
		{
			ParallelOptions parallelOptions = new ParallelOptions();
			parallelOptions.MaxDegreeOfParallelism = 16;
			Parallel.For(0, numChunks, parallelOptions, (int chunkNum) =>
			{
				if (!reporter.CancelRequested)
				{
					byte[][,] byteValues = new byte[rowsPerChunk][,];
					int rowsPerChunk2 = rowsPerChunk;
					if (chunkNum == numChunks - 1)
					{
						rowsPerChunk2 -= numChunks * rowsPerChunk - tileCount;
					}
					int maxValue = int.MaxValue;
					int minValue = int.MinValue;
					int minValue2 = int.MinValue;
					int maxValue2 = int.MaxValue;
					for (int i = 0; i < rowsPerChunk2; i++)
					{
						int chunkNumRowsPerChunkTerm = chunkNum * rowsPerChunk + i;
						int tilesWideTerm = chunkNumRowsPerChunkTerm % tilesWide;
						int tilesWideTerm2 = chunkNumRowsPerChunkTerm / tilesWide;
						maxValue = Math.Min(tilesWideTerm, maxValue);
						minValue = Math.Max(tilesWideTerm, minValue);
						minValue2 = Math.Max(tilesWideTerm2, minValue2);
						maxValue2 = Math.Min(tilesWideTerm2, maxValue2);
					}
					for (int j = 0; j < rowsPerChunk2; j++)
					{
						int chunkNumRowsPerChunkTerm2 = chunkNum * rowsPerChunk + j;
						byte[,] curMask = (byteValues[j] = CreateNaiiveRenderBlockMask(chunkNumRowsPerChunkTerm2, pyramidLevel));
						Extent other = GetTileExtents(pyramidLevel, chunkNumRowsPerChunkTerm2 % tilesWide, chunkNumRowsPerChunkTerm2 / tilesWide).MoveDown(0.5 * curCellSize).MoveRight(0.5 * curCellSize);
						for (int k = 0; k < _intersectingRasters.Count; k++)
						{
							Raster raster2 = _intersectingRasters[k];
							int pLevelsHigher3 = pLevelsHigher[k];
							double cellSize2 = raster2.GetCellSize(pLevelsHigher3);
							int numTiles2 = raster2.GetNumTiles(pLevelsHigher3);
							int tileWidth2 = raster2.GetTileWidth(pLevelsHigher3);
							int tileHeight2 = raster2.GetTileHeight(pLevelsHigher3);
							int numTilesWide2 = raster2.GetNumTilesWide(pLevelsHigher3);
							raster2.GetNumTilesTall(pLevelsHigher3);
							raster2.GetNumRows(pLevelsHigher3);
							raster2.GetNumCols(pLevelsHigher3);
							byte[] inOutRenderBlockTileStat = inOutRenderBlockTileStats[raster2.RasterPriorityLevel][pLevelsHigher3];
							for (int l = 0; l < numTiles2; l++)
							{
								_ = l % numTilesWide2;
								_ = l / numTilesWide2;
								Extent tileCorners = higherRenderBlockTileCornersPerRaster[k][l];
								Extent extent3 = higherRenderBlockTileCornersExtendedPerRaster[k][l];
								if (extent3.Overlaps(in other) && inOutRenderBlockTileStat[l] != 0)
								{
									if (inOutRenderBlockTileStat[l] == 1)
									{
										Extent extents = higherRenderBlockTileCornersPerRaster[k][l];
										extents = raster2.RestrictExtentsToRaster(extents, pLevelsHigher3);
										extents.Extend(0.25 * cellSize2);
										Block(curMask, other, extents, tileWidth, tileHeight, curCellSize);
									}
									else
									{
										byte[,] values = tlGetByteMaskBuffer.Value;
										lock (h5lock)
										{
											_h5w.ReadRows(EXPERIMENTAL_READERS[k], l, 1, ref values);
										}
										List<Intersect> intersects = new List<Intersect>();
										for (int m = 0; m < tileHeight2; m++)
										{
											Intersect intersect = default;
											for (int n = 0; n < tileWidth2; n++)
											{
												int tileWidth2Term = m * tileWidth2 + n;
												if (values[0, tileWidth2Term] == 1 && (n == 0 || values[0, tileWidth2Term - 1] == 0))
												{
													intersect.Start = n;
												}
												if (values[0, tileWidth2Term] == 1 && (n == tileWidth2 - 1 || values[0, tileWidth2Term + 1] == 0))
												{
													intersect.Stop = n;
													intersects.Add(intersect);
													intersect = default;
												}
											}
											foreach (Intersect intersect2 in intersects)
											{
												Extent cellExtents = GetCellExtents(tileCorners, cellSize2, intersect2.Start, m);
												Extent cellExtents2 = GetCellExtents(tileCorners, cellSize2, intersect2.Stop, m);
												Extent blockExtents = new Extent(cellExtents.Left, cellExtents2.Right, cellExtents2.Bot, cellExtents.Top).Extend(0.25 * cellSize2);
												Block(curMask, other, blockExtents, tileWidth, tileHeight, curCellSize);
											}
											intersects.Clear();
										}
									}
								}
							}
						}
					}
					int count = tileWidth * tileHeight;
					byte[,] byteValues2 = new byte[rowsPerChunk2, count];
					for (int intervalIndex3 = 0; intervalIndex3 < rowsPerChunk2; intervalIndex3++)
					{
						int chunkNumRowsPerChunkTerm3 = chunkNum * rowsPerChunk + intervalIndex3;
						Buffer.BlockCopy(byteValues[intervalIndex3], 0, byteValues2, intervalIndex3 * count, count);
						bool condition = true;
						bool condition2 = true;
						byte[,] byteValue = byteValues[intervalIndex3];
						for (int intervalIndex4 = 0; intervalIndex4 < byteValue.GetLength(0); intervalIndex4++)
						{
							for (int intervalIndex5 = 0; intervalIndex5 < byteValue.GetLength(1); intervalIndex5++)
							{
								if (byteValue[intervalIndex4, intervalIndex5] == 1)
								{
									condition2 = false;
								}
								else
								{
									condition = false;
								}
								if (!condition && !condition2)
								{
									break;
								}
							}
							if (!condition && !condition2)
							{
								break;
							}
						}
						if (condition)
						{
							inOutRenderBlockTileStats[RasterPriorityLevel][pyramidLevel][chunkNumRowsPerChunkTerm3] = 1;
						}
						else if (condition2)
						{
							inOutRenderBlockTileStats[RasterPriorityLevel][pyramidLevel][chunkNumRowsPerChunkTerm3] = 0;
						}
						else
						{
							inOutRenderBlockTileStats[RasterPriorityLevel][pyramidLevel][chunkNumRowsPerChunkTerm3] = 4;
						}
					}
					lock (h5lock)
					{
						_h5w.AddRows(EXPERIMENTAL_WRITER, byteValues2, rowsPerChunk2, chunkNum * rowsPerChunk);
					}
					ReportTilesDone(rowsPerChunk2);
				}
			});
		}
		catch
		{
			throw;
		}
		finally
		{
			EXPERIMENTAL_WRITER.Dispose();
			H5DatasetID[] experimentaL_READERS = EXPERIMENTAL_READERS;
			for (int intervalIndex6 = 0; intervalIndex6 < experimentaL_READERS.Length; intervalIndex6++)
			{
				experimentaL_READERS[intervalIndex6].Dispose();
			}
		}
	}

	public void ExportMaskToBIL(int plevel, string MaskFileOut)
	{
		string changeExtension = Path.ChangeExtension(MaskFileOut, "hdr");
		File.Delete(MaskFileOut);
		File.Delete(changeExtension);
		string maskName = Terrain.GetMaskName(_tiffFileIn, plevel);
		double cellSize = 0.0;
		int numCols = 0;
		int numRows = 0;
		using (FileStream output = new FileStream(MaskFileOut, FileMode.CreateNew))
		{
			using BinaryWriter binaryWriter = new BinaryWriter(output);
			GetNoData(plevel);
			numCols = GetNumCols(plevel);
			numRows = GetNumRows(plevel);
			int tileWidth = GetTileWidth(plevel);
			int tileHeight = GetTileHeight(plevel);
			int numTilesWide = GetNumTilesWide(plevel);
			int numTilesTall = GetNumTilesTall(plevel);
			cellSize = GetCellSize(plevel);
			int count = numTilesWide * numTilesTall;
			byte[,] values = null;
			for (int i = 0; i < count; i++)
			{
				int numTilesWideTerm = i % numTilesWide;
				int numTilesWideTerm2 = i / numTilesWide;
				int count2 = Math.Min(tileWidth, numCols - tileWidth * numTilesWideTerm);
				int count3 = Math.Min(tileHeight, numRows - tileHeight * numTilesWideTerm2);
				_h5w.ReadRows(maskName, i, 1, ref values);
				int offset = numCols * tileHeight * numTilesWideTerm2 + tileWidth * numTilesWideTerm;
				binaryWriter.Seek(offset, SeekOrigin.Begin);
				for (int j = 0; j < count3; j++)
				{
					byte[] byteValues = new byte[count2];
					Buffer.BlockCopy(values, j * tileWidth, byteValues, 0, byteValues.Length);
					binaryWriter.Write(byteValues);
					if (j + 1 < count3)
					{
						binaryWriter.Seek(numCols - count2, SeekOrigin.Current);
					}
				}
			}
		}
		Extent ext = GetCorners().MoveRight(cellSize * 0.5).MoveDown(cellSize * 0.5);
		HDR.MakeHDR(ext, cellSize, numCols, numRows, changeExtension, 0f);
	}

	public Extent GetCorners()
	{
		return _storedExtent;
	}

	public float[] GetTile(int tileIdx, int pLevel)
	{
		TiffDirectory tiffDirectory = _ftr.Directories[pLevel];
		float[] tile = new float[tiffDirectory.Metadata.TileWidth * tiffDirectory.Metadata.TileHeight];
		_ftr.Directories[pLevel].GetTile<float>(tileIdx, tile);
		return tile;
	}

	public string GetMaskName(int pyrLevel = 0)
	{
		return Terrain.GetMaskName(_tiffFileIn, pyrLevel);
	}

	public string GetStatsName()
	{
		return Terrain.GetMinMaxName(_tiffFileIn, 0);
	}

	public string GetTiffName()
	{
		return _tiffFileIn;
	}

	public int GetNumDirectories()
	{
		return _ftr.Directories.Count;
	}

	public double GetCellSize(int Plevel)
	{
		return _cellsize[Plevel];
	}

	public int GetNumRows(int Plevel)
	{
		return _imgHeight[Plevel];
	}

	public int GetNumCols(int Plevel)
	{
		return _imgWidth[Plevel];
	}

	public int GetNumTiles(int Plevel)
	{
		return _numTiles[Plevel];
	}

	public int GetTileWidth(int Plevel)
	{
		return _tileWidth[Plevel];
	}

	public int GetTileHeight(int Plevel)
	{
		return _tileHeight[Plevel];
	}

	public int GetNumTilesWide(int Plevel)
	{
		return _numTilesWide[Plevel];
	}

	public int GetNumTilesTall(int Plevel)
	{
		return _numTilesTall[Plevel];
	}

	public float GetNoData(int Plevel)
	{
		return _nodata[Plevel];
	}

	public Extent GetExtentsNoDeadSpace(int PLevel)
	{
		return _extentsNoDeadSpace[PLevel];
	}

	private Extent GetTileExtents(int plevel, int tileNumX, int tileNumY)
	{
		int tileWidth = GetTileWidth(plevel);
		int tileHeight = GetTileHeight(plevel);
		int tileHeightGetNumTilesTallTerm = tileHeight * GetNumTilesTall(plevel);
		int tileWidthGetNumTilesWideTerm = tileWidth * GetNumTilesWide(plevel);
		double upperRightExtentsWithDeadSpaceTerm = _extentsWithDeadSpace[plevel].UpperRight.X - _extentsWithDeadSpace[plevel].UpperLeft.X;
		double upperRightExtentsWithDeadSpaceTerm2 = _extentsWithDeadSpace[plevel].UpperRight.Y - _extentsWithDeadSpace[plevel].LowerRight.Y;
		double minX = _extentsWithDeadSpace[plevel].UpperLeft.X + upperRightExtentsWithDeadSpaceTerm * ((double)(tileNumX * tileWidth) / (double)tileWidthGetNumTilesWideTerm);
		double maxX = _extentsWithDeadSpace[plevel].UpperLeft.X + upperRightExtentsWithDeadSpaceTerm * ((double)((tileNumX + 1) * tileWidth) / (double)tileWidthGetNumTilesWideTerm);
		double maxY = _extentsWithDeadSpace[plevel].UpperLeft.Y - upperRightExtentsWithDeadSpaceTerm2 * ((double)(tileNumY * tileHeight) / (double)tileHeightGetNumTilesTallTerm);
		double minY = _extentsWithDeadSpace[plevel].UpperLeft.Y - upperRightExtentsWithDeadSpaceTerm2 * ((double)((tileNumY + 1) * tileHeight) / (double)tileHeightGetNumTilesTallTerm);
		return new Extent(minX, maxX, minY, maxY);
	}

	public Extent RestrictExtentsToRaster(Extent extents, int pLevel)
	{
		return _extentsNoDeadSpace[pLevel].Intersection(extents);
	}

	private static Extent GetCellExtents(Extent TileCorners, double cellSize, int cellX, int cellY)
	{
		double minX = TileCorners.LowerLeft.X + cellSize * (double)cellX;
		double maxX = TileCorners.LowerLeft.X + cellSize * (double)(cellX + 1);
		double maxY = TileCorners.UpperLeft.Y - cellSize * (double)cellY;
		double minY = TileCorners.UpperLeft.Y - cellSize * (double)(cellY + 1);
		return new Extent(minX, maxX, minY, maxY);
	}
}
