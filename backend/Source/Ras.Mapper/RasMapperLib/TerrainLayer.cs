using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Geospatial.GDALAssist;
using Geospatial.Rasters;
using Geospatial.Terrain;
using H5Assist.Caching.VB;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Terrain;
using RasMapperLib.TinHelpers;
using RasMapperLib.Utilities;
using TiffAssist;
using TiffBinaryReader;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace RasMapperLib;

public class TerrainLayer : InterpolatedLayer
{
	public class RenderTileData
	{
		private H5Reader _hr;

		private FloatTiffReader _tr;

		private RasterFileInfo _rfi;

		private RasterM _tileRaster;

		private float[] _tileValues;

		private byte[,] _mask;

		private float[,] _perimeterValues;

		public RasterM TileWithPerimeterRaster;

		public float[] TileWithPerimeterValues;

		private bool[] _tileWithPerimeterMask;

		public bool IsTileCompletelyMasked;

		private float _a;

		private float _b;

		private float _c;

		private float _d;

		private float _e;

		public RenderTileData(H5Reader hr)
		{
			_tileRaster = new RasterM();
			IsTileCompletelyMasked = false;
			_hr = hr;
			int ordinal = 256;
			checked
			{
				_tileValues = new float[ordinal * ordinal - 1 + 1];
				_mask = new byte[1, ordinal * ordinal - 1 + 1];
				_perimeterValues = new float[1, ordinal * 2 + 1];
				int ordinal2 = (ordinal + 1) * (ordinal + 1);
				TileWithPerimeterValues = new float[ordinal2 - 1 + 1];
				_tileWithPerimeterMask = new bool[ordinal2 - 1 + 1];
			}
		}

		public void SetRaster(FloatTiffReader tr, RasterFileInfo rfi)
		{
			_tr = tr;
			_rfi = rfi;
		}

		public bool SetTile(int pyramidLevel, int tileIdx)
		{
			if (!_rfi.TileHasAnyData[pyramidLevel][tileIdx])
			{
				return false;
			}
			_rfi.TileRasters[pyramidLevel].SetCellTileRasterM(tileIdx, _rfi.TileSize[pyramidLevel], _tileRaster);
			TileWithPerimeterRaster = _tileRaster.ExtendRaster(0, 1, 0, 1);
			_tr.GetTile(pyramidLevel, tileIdx, ref _tileValues);
			string groupnameRfiTerm = _rfi.Groupname + "/" + Strings.Format(pyramidLevel);
			_hr.ReadRows(groupnameRfiTerm + "/Mask", tileIdx, 1, ref _mask);
			_hr.ReadRows(groupnameRfiTerm + "/Perimeter", tileIdx, 1, ref _perimeterValues);
			Raster.CombineTileWithPerim(_tileValues, _perimeterValues, 256, 256, ref TileWithPerimeterValues);
			Buffers.ArraySetAllVB(_tileWithPerimeterMask, v: false);
			int ordinal = 0;
			int ordinal2 = 0;
			checked
			{
				int count = _tileRaster.Rows - 1;
				for (int i = 0; i <= count; i++)
				{
					int count2 = _tileRaster.Cols - 1;
					for (int j = 0; j <= count2; j++)
					{
						if ((_mask[0, ordinal] & 1) > 0)
						{
							_tileWithPerimeterMask[ordinal2] = true;
							_tileWithPerimeterMask[ordinal2 + 1] = true;
							_tileWithPerimeterMask[ordinal2 + TileWithPerimeterRaster.Cols] = true;
							_tileWithPerimeterMask[ordinal2 + TileWithPerimeterRaster.Cols + 1] = true;
						}
						ordinal++;
						ordinal2++;
					}
					ordinal2++;
				}
				IsTileCompletelyMasked = true;
				int count3 = TileWithPerimeterValues.Count() - 1;
				for (int k = 0; k <= count3; k++)
				{
					if (!_tileWithPerimeterMask[k])
					{
						TileWithPerimeterValues[k] = -9999f;
					}
					else
					{
						IsTileCompletelyMasked = false;
					}
				}
				return true;
			}
		}

		public bool SetRenderBlock(int rbRow, int rbCol)
		{
			if ((_mask[0, _tileRaster.CellIndex(rbRow, rbCol)] & 1) == 0)
			{
				return false;
			}
			int cellIndex = TileWithPerimeterRaster.CellIndex(rbRow, rbCol);
			_a = TileWithPerimeterValues[cellIndex];
			checked
			{
				_b = TileWithPerimeterValues[cellIndex + 1];
				_c = TileWithPerimeterValues[cellIndex + TileWithPerimeterRaster.Cols + 1];
				_d = TileWithPerimeterValues[cellIndex + TileWithPerimeterRaster.Cols];
				_e = (_a + _b + _c + _d) * 0.25f;
				return true;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__113_002D0
	{
		public List<RasMapperLib.Terrain.TileColumn> tileColumns;

		public RasterM tileRaster;

		public RasterFileInfo rfi;

		public int level;

		public TiffFile tr;

		public object h5lock;

		public RasterM raster;

		public float[] byvalValues;

		public object stitchLock;

		public List<SegmentM> byvalStitches;

		public TerrainLayer terrainLayer;

		public _Closure_0024__113_002D0(_Closure_0024__113_002D0 other)
		{
			if (other != null)
			{
				tileColumns = other.tileColumns;
				tileRaster = other.tileRaster;
				rfi = other.rfi;
				level = other.level;
				tr = other.tr;
				h5lock = other.h5lock;
				raster = other.raster;
				byvalValues = other.byvalValues;
				stitchLock = other.stitchLock;
				byvalStitches = other.byvalStitches;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(RasMapperLib.Terrain.TileRow tRow)
		{
			_Closure_0024__113_002D1 localValue = new _Closure_0024__113_002D1(null);
			localValue.outerClosure = this;
			localValue.tRow = tRow;
			localValue.localTile = new RasterM();
			List<SegmentM> segmentMs = new List<SegmentM>();
			int index = localValue.tRow.Index;
			using (List<RasMapperLib.Terrain.TileColumn>.Enumerator tileColumns = this.tileColumns.GetEnumerator())
			{
				_Closure_0024__113_002D4 localValue2 = default;
				_Closure_0024__113_002D2 localValue3 = default;
				_Closure_0024__113_002D3 localValue4 = default;
				while (tileColumns.MoveNext())
				{
					localValue2 = new _Closure_0024__113_002D4(localValue2);
					localValue2.outerClosure = localValue;
					localValue2.tCol = tileColumns.Current;
					localValue3 = new _Closure_0024__113_002D2(localValue3);
					localValue3.outerClosure = localValue2;
					int index2 = localValue3.outerClosure.tCol.Index;
					int cellIndex = tileRaster.CellIndex(index, index2);
					if (!rfi.TileHasAnyData[level][cellIndex])
					{
						continue;
					}
					tileRaster.SetCellTileRasterM(cellIndex, rfi.TileSize[level], localValue3.outerClosure.outerClosure.localTile);
					TileDescriptor key = new TileDescriptor(rfi.Filename, level, cellIndex);
					if (terrainLayer._tileCacheConcurrent.ContainsKey(key))
					{
						localValue3.tileData = terrainLayer._tileCacheConcurrent[key];
					}
					else
					{
						localValue3.tileData = new TileData();
						localValue3.tileData.tileValues = tr.Directories[level].GetTileVB<float>(cellIndex);
						object h5lock = this.h5lock;
						ObjectFlowControl.CheckForSyncLockOnValueType(h5lock);
						bool lockTaken = false;
						try
						{
							Monitor.Enter(h5lock, ref lockTaken);
							localValue3.tileData.perimeterValues = terrainLayer._hdf5PerimeterRowCache.EnsureGetRow(rfi.Groupname + "/" + Strings.Format(level) + "/Perimeter", cellIndex);
							localValue3.tileData.mask = terrainLayer._hdf5MaskRowCache.EnsureGetRow(rfi.Groupname + "/" + Strings.Format(level) + "/Mask", cellIndex);
						}
						finally
						{
							if (lockTaken)
							{
								Monitor.Exit(h5lock);
							}
						}
					}
					if (terrainLayer._tileCacheConcurrent.TryAdd(key, localValue3.tileData))
					{
						localValue3.tileData.TimeLastUsed = checked(Interlocked.Increment(ref terrainLayer._tileCount) - 1);
					}
					else
					{
						localValue3.tileData.TimeLastUsed = terrainLayer._tileCount;
					}
					int count = terrainLayer._tileCacheConcurrent.Count;
					if (count > 50)
					{
						terrainLayer.ClearLRUTilesConcurrent(5, count);
					}
					localValue4 = new _Closure_0024__113_002D3(localValue4);
					localValue4.outerClosure = localValue3;
					localValue4.threadLocalRenderBlockGetter = new ThreadLocal<RasMapperLib.Terrain.RenderBlock>([SpecialName] () =>
					{
						RasMapperLib.Terrain.RenderBlock renderBlock = new RasMapperLib.Terrain.RenderBlock();
						renderBlock.SetTile(localValue4.outerClosure.outerClosure.outerClosure.localTile, localValue4.outerClosure.tileData.tileValues, localValue4.outerClosure.tileData.perimeterValues, localValue4.outerClosure.tileData.mask, new List<SegmentM>());
						return renderBlock;
					}, trackAllValues: true);
					System.Threading.Tasks.Parallel.For(0, localValue4.outerClosure.outerClosure.outerClosure.tRow.RenderBlockRows.Count, localValue4._Lambda_0024__2);
					foreach (RasMapperLib.Terrain.RenderBlock renderBlock2 in localValue4.threadLocalRenderBlockGetter.Values)
					{
						segmentMs.AddRange(renderBlock2.Stitches);
					}
					localValue4.threadLocalRenderBlockGetter.Dispose();
				}
			}
			object stitchLock = this.stitchLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(stitchLock);
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(stitchLock, ref lockTaken2);
				byvalStitches.AddRange(segmentMs);
			}
			finally
			{
				if (lockTaken2)
				{
					Monitor.Exit(stitchLock);
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__113_002D1
	{
		public RasterM localTile;

		public RasMapperLib.Terrain.TileRow tRow;

		public _Closure_0024__113_002D0 outerClosure;

		public _Closure_0024__113_002D1(_Closure_0024__113_002D1 other)
		{
			if (other != null)
			{
				localTile = other.localTile;
				tRow = other.tRow;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__113_002D2
	{
		public TileData tileData;

		public _Closure_0024__113_002D4 outerClosure;

		public _Closure_0024__113_002D2(_Closure_0024__113_002D2 other)
		{
			if (other != null)
			{
				tileData = other.tileData;
			}
		}

		[SpecialName]
		internal RasMapperLib.Terrain.RenderBlock _Lambda_0024__1()
		{
			RasMapperLib.Terrain.RenderBlock renderBlock = new RasMapperLib.Terrain.RenderBlock();
			renderBlock.SetTile(outerClosure.outerClosure.localTile, tileData.tileValues, tileData.perimeterValues, tileData.mask, new List<SegmentM>());
			return renderBlock;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__113_002D3
	{
		public ThreadLocal<RasMapperLib.Terrain.RenderBlock> threadLocalRenderBlockGetter;

		public _Closure_0024__113_002D2 outerClosure;

		public _Closure_0024__113_002D3(_Closure_0024__113_002D3 other)
		{
			if (other != null)
			{
				threadLocalRenderBlockGetter = other.threadLocalRenderBlockGetter;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__2(int rbrIdx)
		{
			RasMapperLib.Terrain.RenderBlockRow renderBlockRow = outerClosure.outerClosure.outerClosure.tRow.RenderBlockRows[rbrIdx];
			RasMapperLib.Terrain.RenderBlock renderBlock = threadLocalRenderBlockGetter.Value;
			foreach (RasMapperLib.Terrain.RenderBlockColumn renderBlockColumn in outerClosure.outerClosure.tCol.RenderBlockColumns)
			{
				if (!renderBlock.SetRowCol(renderBlockRow.index, renderBlockColumn.index))
				{
					continue;
				}
				foreach (RasMapperLib.Terrain.RasterRow rasterRow in renderBlockRow.RasterRows)
				{
					foreach (RasMapperLib.Terrain.RasterColumn rasterColumn in renderBlockColumn.RasterColumns)
					{
						int indexRasterColumnTerm = checked(rasterColumn.Index + rasterRow.Index * outerClosure.outerClosure.outerClosure.outerClosure.raster.Cols);
						if (outerClosure.outerClosure.outerClosure.outerClosure.byvalValues[indexRasterColumnTerm] == -9999f)
						{
							outerClosure.outerClosure.outerClosure.outerClosure.byvalValues[indexRasterColumnTerm] = renderBlock.Interpolate(rasterColumn.Fraction, rasterRow.Fraction);
						}
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__113_002D4
	{
		public RasMapperLib.Terrain.TileColumn tCol;

		public _Closure_0024__113_002D1 outerClosure;

		public _Closure_0024__113_002D4(_Closure_0024__113_002D4 other)
		{
			if (other != null)
			{
				tCol = other.tCol;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__115_002D0
	{
		public byte[,] rbMask;

		public byte[] cellMask;

		public float[] lvals;

		public _Closure_0024__115_002D0(_Closure_0024__115_002D0 other)
		{
			if (other != null)
			{
				rbMask = other.rbMask;
				cellMask = other.cellMask;
				lvals = other.lvals;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			bool systemStartTerm = System.Math.Floor((double)start / 256.0) == 255.0;
			checked
			{
				int finishOffset = finish - 1;
				for (int i = start; i <= finishOffset; i++)
				{
					if ((rbMask[0, i] & 1) == 1)
					{
						cellMask[i] = 1;
						cellMask[i + 1] = 1;
						if (!systemStartTerm)
						{
							cellMask[i + 256] = 1;
							cellMask[i + 257] = 1;
						}
					}
				}
				if ((rbMask[0, finish] & 1) == 1)
				{
					cellMask[finish] = 1;
					if (!systemStartTerm)
					{
						cellMask[finish + 256] = 1;
					}
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (cellMask[i] == 0)
				{
					lvals[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__116_002D0
	{
		public byte[,] rbMask;

		public byte[] cellMask;

		public float[] lvals;

		public _Closure_0024__116_002D0(_Closure_0024__116_002D0 other)
		{
			if (other != null)
			{
				rbMask = other.rbMask;
				cellMask = other.cellMask;
				lvals = other.lvals;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int start, int finish)
		{
			bool systemStartTerm = System.Math.Floor((double)start / 256.0) == 255.0;
			checked
			{
				int finishOffset = finish - 1;
				for (int i = start; i <= finishOffset; i++)
				{
					if ((rbMask[0, i] & 1) == 1)
					{
						cellMask[i] = 1;
						cellMask[i + 1] = 1;
						if (!systemStartTerm)
						{
							cellMask[i + 256] = 1;
							cellMask[i + 257] = 1;
						}
					}
				}
				if ((rbMask[0, finish] & 1) == 1)
				{
					cellMask[finish] = 1;
					if (!systemStartTerm)
					{
						cellMask[finish + 256] = 1;
					}
				}
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1(int start, int finish)
		{
			for (int i = start; i <= finish; i = checked(i + 1))
			{
				if (cellMask[i] == 0)
				{
					lvals[i] = -9999f;
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__134_002D0
	{
		public RasterM tiles;

		public RasterFileInfo rfi;

		public Dictionary<int, List<PolylineSegmentStartStation>> tilesToProcess;

		public Dictionary<int, int> tileIdxMap;

		public byte[][] maskRowSplitter;

		public float[][] perimRowSplitter;

		public List<Polyline> polylines;

		public float filterTol;

		public TerrainLayer terrainLayer;

		public _Closure_0024__134_002D0(_Closure_0024__134_002D0 other)
		{
			if (other != null)
			{
				tiles = other.tiles;
				rfi = other.rfi;
				tilesToProcess = other.tilesToProcess;
				tileIdxMap = other.tileIdxMap;
				maskRowSplitter = other.maskRowSplitter;
				perimRowSplitter = other.perimRowSplitter;
				polylines = other.polylines;
				filterTol = other.filterTol;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__134_002D1
	{
		public int pIndex;

		public double segStartStation;

		public _Closure_0024__134_002D0 outerClosure;

		public _Closure_0024__134_002D1(_Closure_0024__134_002D1 other)
		{
			if (other != null)
			{
				pIndex = other.pIndex;
				segStartStation = other.segStartStation;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__134_002D2
	{
		public int segIndex;

		public _Closure_0024__134_002D1 outerClosure;

		public _Closure_0024__134_002D2(_Closure_0024__134_002D2 other)
		{
			if (other != null)
			{
				segIndex = other.segIndex;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int n)
		{
			if (n >= 0 && n <= outerClosure.outerClosure.tiles.CellMaxIndex() && outerClosure.outerClosure.rfi.TileHasAnyData[0][n])
			{
				if (!outerClosure.outerClosure.tilesToProcess.ContainsKey(n))
				{
					outerClosure.outerClosure.tilesToProcess[n] = new List<PolylineSegmentStartStation>();
				}
				outerClosure.outerClosure.tilesToProcess[n].Add(new PolylineSegmentStartStation(outerClosure.pIndex, segIndex, outerClosure.segStartStation));
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__134_002D3
	{
		public ThreadLocal<Dictionary<int, Profile>> threadLocalProfileData;

		public ThreadLocal<float[]> tlPerim;

		public ThreadLocal<float[]> tlTile;

		public ThreadLocal<byte[]> tlMask;

		public _Closure_0024__134_002D4 outerClosure;

		public _Closure_0024__134_002D3(_Closure_0024__134_002D3 other)
		{
			if (other != null)
			{
				threadLocalProfileData = other.threadLocalProfileData;
				tlPerim = other.tlPerim;
				tlTile = other.tlTile;
				tlMask = other.tlMask;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__6(KeyValuePair<int, List<PolylineSegmentStartStation>> t2p)
		{
			int key = t2p.Key;
			if (!outerClosure.outerClosure.rfi.TileHasAnyData[0][key])
			{
				return;
			}
			Dictionary<int, Profile> profileByInt = threadLocalProfileData.Value;
			List<PolylineSegmentStartStation> polylineSegmentStartStations = t2p.Value;
			RasterM rasterM = new RasterM();
			outerClosure.outerClosure.tiles.SetCellTileRasterM(key, outerClosure.outerClosure.rfi.TileSize[0], rasterM);
			RasterizeSegment tileRS = new RasterizeSegment(rasterM);
			float[] floatValues = tlPerim.Value;
			float[] floatValuesArray = tlTile.Value;
			byte[] byteValues = tlMask.Value;
			floatValuesArray = outerClosure.tr.Directories[0].GetTileVB<float>(key);
			int tileIdxMap = outerClosure.outerClosure.tileIdxMap[key];
			checked
			{
				byte[] src = outerClosure.outerClosure.maskRowSplitter[(int)System.Math.Floor((double)tileIdxMap / 1000.0)];
				float[] src2 = outerClosure.outerClosure.perimRowSplitter[(int)System.Math.Floor((double)tileIdxMap / 1000.0)];
				tileIdxMap = unchecked(tileIdxMap % 1000);
				Buffer.BlockCopy(src, tileIdxMap * byteValues.Length, byteValues, 0, byteValues.Length);
				Buffer.BlockCopy(src2, tileIdxMap * floatValues.Length * 4, floatValues, 0, floatValues.Length * 4);
				RasMapperLib.Terrain.RenderBlock renderBlock = new RasMapperLib.Terrain.RenderBlock();
				renderBlock.SetTile(rasterM, floatValuesArray, floatValues, byteValues, null);
				int countOffset = polylineSegmentStartStations.Count - 1;
				for (int i = 0; i <= countOffset; i++)
				{
					PolylineSegmentStartStation polylineSegmentStartStation = polylineSegmentStartStations[i];
					int polylineIndex = polylineSegmentStartStation.PolylineIndex;
					int segmentIndex = polylineSegmentStartStation.SegmentIndex;
					Profile profile = null;
					if (!profileByInt.TryGetValue(polylineIndex, out profile))
					{
						profile = (profileByInt[polylineIndex] = new Profile());
					}
					SegmentM segment = outerClosure.outerClosure.polylines[polylineIndex].Segment(segmentIndex);
					double segmentStartingStation = polylineSegmentStartStation.SegmentStartingStation;
					Profile profile2 = outerClosure.outerClosure.terrainLayer.ComputeProfilesOnTile(rasterM, tileRS, renderBlock, segment, segmentStartingStation);
					if (float.IsNaN(outerClosure.outerClosure.filterTol) | (outerClosure.outerClosure.filterTol <= 0f))
					{
						profile.AddRange(profile2);
						continue;
					}
					List<int> filterKeepList = profile2.FilterKeepList(outerClosure.outerClosure.filterTol);
					foreach (int filterKeepList2 in filterKeepList)
					{
						profile.Add(profile2[filterKeepList2]);
					}
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__134_002D4
	{
		public TiffFile tr;

		public _Closure_0024__134_002D0 outerClosure;

		public _Closure_0024__134_002D4(_Closure_0024__134_002D4 other)
		{
			if (other != null)
			{
				tr = other.tr;
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__135_002D0
	{
		public RasMapperLib.Terrain.RenderBlock renderblock;

		public SegmentM triEdge;

		public SegmentM segment;

		public Profile profile;

		public double startingStation;

		public double segLength;

		public _Closure_0024__135_002D0(_Closure_0024__135_002D0 other)
		{
			if (other != null)
			{
				renderblock = other.renderblock;
				triEdge = other.triEdge;
				segment = other.segment;
				profile = other.profile;
				startingStation = other.startingStation;
				segLength = other.segLength;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int rbRow, int rbCol)
		{
			if (!renderblock.SetRowCol(rbRow, rbCol))
			{
				return;
			}
			PointM pointM = renderblock.PointA(rbRow, rbCol);
			PointM pointM2 = renderblock.PointB(rbRow, rbCol);
			PointM pointM3 = renderblock.PointC(rbRow, rbCol);
			PointM pointM4 = renderblock.PointD(rbRow, rbCol);
			PointM pA = renderblock.PointE(rbRow, rbCol);
			int ordinal = 0;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			do
			{
				switch (ordinal)
				{
				case 0:
					triEdge.Assign(pA, pointM);
					break;
				case 1:
					triEdge.B = pointM2;
					break;
				case 2:
					triEdge.B = pointM3;
					break;
				case 3:
					triEdge.B = pointM4;
					break;
				case 4:
					triEdge.Assign(pointM, pointM2);
					break;
				case 5:
					triEdge.Assign(pointM2, pointM3);
					break;
				case 6:
					triEdge.Assign(pointM3, pointM4);
					break;
				case 7:
					triEdge.Assign(pointM4, pointM);
					break;
				}
				if (segment.Intersects(triEdge, ref x, ref y, ref t, ref t2))
				{
					float y2 = (float)triEdge.ZAtFraction(t2);
					profile.Add((float)(startingStation + segLength * t), y2);
				}
				ordinal = checked(ordinal + 1);
			}
			while (ordinal <= 7);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__136_002D0
	{
		public RasMapperLib.Terrain.RenderBlock renderblock;

		public SegmentM triEdge;

		public SegmentM segment;

		public Profile profile;

		public double startingStation;

		public double segLength;

		public _Closure_0024__136_002D0(_Closure_0024__136_002D0 other)
		{
			if (other != null)
			{
				renderblock = other.renderblock;
				triEdge = other.triEdge;
				segment = other.segment;
				profile = other.profile;
				startingStation = other.startingStation;
				segLength = other.segLength;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0(int rbRow, int rbCol)
		{
			if (!renderblock.SetRowCol(rbRow, rbCol))
			{
				return;
			}
			PointM pointM = renderblock.PointA(rbRow, rbCol);
			PointM pointM2 = renderblock.PointB(rbRow, rbCol);
			PointM pointM3 = renderblock.PointC(rbRow, rbCol);
			PointM pointM4 = renderblock.PointD(rbRow, rbCol);
			PointM pA = renderblock.PointE(rbRow, rbCol);
			int ordinal = 0;
			double x = default;
			double y = default;
			double t = default;
			double t2 = default;
			do
			{
				switch (ordinal)
				{
				case 0:
					triEdge.Assign(pA, pointM);
					break;
				case 1:
					triEdge.B = pointM2;
					break;
				case 2:
					triEdge.B = pointM3;
					break;
				case 3:
					triEdge.B = pointM4;
					break;
				case 4:
					triEdge.Assign(pointM, pointM2);
					break;
				case 5:
					triEdge.Assign(pointM2, pointM3);
					break;
				case 6:
					triEdge.Assign(pointM3, pointM4);
					break;
				case 7:
					triEdge.Assign(pointM4, pointM);
					break;
				}
				if (segment.Intersects(triEdge, ref x, ref y, ref t, ref t2))
				{
					float y2 = (float)triEdge.ZAtFraction(t2);
					profile.Add((float)(startingStation + segLength * t), y2);
				}
				ordinal = checked(ordinal + 1);
			}
			while (ordinal <= 7);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__98_002D0
	{
		public Extent fileRasterM;

		public _Closure_0024__98_002D0(_Closure_0024__98_002D0 other)
		{
			if (other != null)
			{
				fileRasterM = other.fileRasterM;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Extent ext)
		{
			return fileRasterM.Overlaps(ext);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__98_002D1
	{
		public RasterM tileRasterM;

		public _Closure_0024__98_002D1(_Closure_0024__98_002D1 other)
		{
			if (other != null)
			{
				tileRasterM = other.tileRasterM;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__1(Extent ext)
		{
			return tileRasterM.Overlaps(ext);
		}
	}

	public const string sPlotRasterFileOutlines = "Plot raster file outlines";

	public const string sPlotRasterFilenames = "Plot raster file names";

	public const string sPlotTileOutlines = "Plot tile outlines";

	public const string sPlotCellOutlines = "Plot cell outlines (when zoomed in)";

	public const string sPlotCellValues = "Plot cell values (when zoomed in)";

	public const string sPlotStitchTIN = "Plot stitch TIN edges";

	public const string sPlotLevel0StitchTIN = "Plot Level0 stitch TIN edges";

	public const string sPlotRemoveStitches = "Remove Stitch Rendering";

	public const string shdfFilter = "Terrain (*.hdf)|*.hdf";

	private const int MaxTileCount = 50;

	public const string RootTerrainGroup = "/Terrain";

	public const string sTerrainFolder = "Terrain";

	private Histogram _histogram;

	private long _tileCount;

	private Dictionary<TileDescriptor, TileData> _tileCache;

	private ConcurrentDictionary<TileDescriptor, TileData> _tileCacheConcurrent;

	private CacheCollection _hdf5Cache;

	private H5RowCache<float> _hdf5PerimeterRowCache;

	private H5RowCache<byte> _hdf5MaskRowCache;

	private H5RowCache<float> _hdf5MinMaxNoDataRowCache;

	private Tin _level0StitchTIN;

	private List<SegmentM> _level0Stitches;

	private bool _triedStitchReading;

	private DistinctSpatialIndex _stitchTINSpatialIndex;

	private List<SegmentM> _stitches;

	private Tin _stitchTIN;

	private RasterFileInfo[] _rasterFilesInfo;

	private bool _useParameters;

	private ElevationModificationGroup _modificationGroup;

	private List<int> _debugFilterDisable;

	private Extent _rasterFilesExtent;

	private bool _hasLoadedRasterFiles;

	public ElevationModificationGroup Modifications => _modificationGroup;

	public override bool CanRename { get; }

	public override bool CanMoveToAnotherLevel { get; }

	public override Type BaseGroupType { get; }

	public string DataType { get; }

	public override string BalloonHighlightStringFormat
	{
		get
		{
			string result = "0.000";
			switch (SharedData.VerticalDecimalPlaces)
			{
			case 0:
				result = "0";
				break;
			case 1:
				result = "0.0";
				break;
			case 2:
				result = "0.00";
				break;
			case 3:
				result = "0.000";
				break;
			case 4:
				result = "0.0000";
				break;
			case 5:
				result = "0.00000";
				break;
			}
			return result;
		}
	}

	public string DataUnits => SharedData.LengthUnits;

	public override Extent Extent
	{
		get
		{
			if (!_hasLoadedRasterFiles)
			{
				ReadRasterFilesInfo();
			}
			_extent = Extent.Union(_modificationGroup.Extent, _rasterFilesExtent);
			return _extent;
		}
	}

	public bool UseParameters
	{
		get
		{
			return _useParameters;
		}
		set
		{
			_useParameters = value;
		}
	}

	public string TerrainName
	{
		get
		{
			if (string.IsNullOrEmpty(SourceFilename))
			{
				return "";
			}
			return System.IO.Path.GetFileNameWithoutExtension(SourceFilename);
		}
	}

	private RasterFileInfo[] RasterFilesInfo
	{
		get
		{
			if (!_hasLoadedRasterFiles)
			{
				ReadRasterFilesInfo();
			}
			return _rasterFilesInfo;
		}
	}

	private RasterFileInfo[] DebugFilteredRasterFilesInfo
	{
		get
		{
			if (_debugFilterDisable.Count == 0)
			{
				return RasterFilesInfo;
			}
			List<RasterFileInfo> rasterFileInfos = new List<RasterFileInfo>();
			RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
			checked
			{
				int count = rasterFilesInfo.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (!_debugFilterDisable.Contains(i))
					{
						rasterFileInfos.Add(rasterFilesInfo[i]);
					}
				}
				return rasterFileInfos.ToArray();
			}
		}
	}

	public Extent RasterFilesExtent => _rasterFilesExtent;

	public int RasterFileCount => RasterFilesInfo.Count();

	public int RasterFilesTileCount
	{
		get
		{
			int cellCountSum = 0;
			checked
			{
				int count = RasterFilesInfo.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					cellCountSum += RasterFilesInfo[i].RenderBlockTiles[0].CellCount;
				}
				return cellCountSum;
			}
		}
	}

	public int[] RasterFilesIndividualTileCount
	{
		get
		{
			checked
			{
				int[] intValues = new int[RasterFilesInfo.Count() - 1 + 1];
				int count = intValues.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					intValues[i] = RasterFilesInfo[i].RenderBlockTiles[0].CellCount;
				}
				return intValues;
			}
		}
	}

	// C# has no syntax for parameterized property 'RasterFilename'.
	public string get_RasterFilename(int index)
	{
		return RasterFilesInfo[index].Filename;
	}

	// C# has no syntax for parameterized property 'RasterBaseName'.
	public string get_RasterBaseName(int index)
	{
		return RasterFilesInfo[index].BaseName;
	}

	// C# has no syntax for parameterized property 'RasterFileExtent'.
	public Extent get_RasterFileExtent(int index)
	{
		return RasterFilesInfo[index].Extent;
	}

	// C# has no syntax for parameterized property 'RasterFileCellSize'.
	public double get_RasterFileCellSize(int index)
	{
		RasterFileInfo rasterFileInfo = RasterFilesInfo[index];
		if (rasterFileInfo == null)
		{
			return double.NaN;
		}
		if (rasterFileInfo.CellSize.IsNullOrEmpty())
		{
			return double.NaN;
		}
		return rasterFileInfo.CellSize[0];
	}

	// C# has no syntax for parameterized property 'RasterFileTileSize'.
	public int get_RasterFileTileSize(int index, int level)
	{
		return RasterFilesInfo[index].TileSize[level];
	}

	// C# has no syntax for parameterized property 'RasterFileRounding'.
	public string get_RasterFileRounding(int index)
	{
		return RasterFilesInfo[index].Rounding;
	}

	public string VRTFilename => System.IO.Path.ChangeExtension(SourceFilename, ".vrt");

	public string TileDatabaseFilename => System.IO.Path.ChangeExtension(SourceFilename, "tiles.db");

	public TerrainLayer()
		: this("Terrain")
	{
	}

	public TerrainLayer(string name)
		: base(name)
	{
		_tileCount = 0L;
		_level0StitchTIN = null;
		_level0Stitches = null;
		_triedStitchReading = false;
		_stitches = null;
		_stitchTIN = null;
		_rasterFilesInfo = null;
		_useParameters = true;
		CanRename = true;
		CanMoveToAnotherLevel = false;
		BaseGroupType = typeof(TerrainsGroup);
		DataType = "Elevation";
		_debugFilterDisable = new List<int>();
		_hasLoadedRasterFiles = false;
		InitializeInternal();
		Nodes.Remove(_modificationGroup);
	}

	public TerrainLayer(string name, string terrainFilename)
		: base(name, terrainFilename)
	{
		_tileCount = 0L;
		_level0StitchTIN = null;
		_level0Stitches = null;
		_triedStitchReading = false;
		_stitches = null;
		_stitchTIN = null;
		_rasterFilesInfo = null;
		_useParameters = true;
		CanRename = true;
		CanMoveToAnotherLevel = false;
		BaseGroupType = typeof(TerrainsGroup);
		DataType = "Elevation";
		_debugFilterDisable = new List<int>();
		_hasLoadedRasterFiles = false;
		InitializeInternal();
		_hdf5PerimeterRowCache = _hdf5Cache.GetRowCache<float>(terrainFilename);
		_hdf5MaskRowCache = _hdf5Cache.GetRowCache<byte>(terrainFilename);
		_modificationGroup.LoadModificationsFromHDF5();
		if (_modificationGroup.AllModifiers().Count() == 0)
		{
			Nodes.Remove(_modificationGroup);
		}
	}

	private void InitializeInternal()
	{
		_tileCache = new Dictionary<TileDescriptor, TileData>();
		_tileCacheConcurrent = new ConcurrentDictionary<TileDescriptor, TileData>();
		_modificationGroup = new ElevationModificationGroup(this);
		_hdf5Cache = new CacheCollection();
		Nodes.Add(_modificationGroup);
	}

	public override List<PlotOption> GetStaticPlotOptions()
	{
		List<PlotOption> staticPlotOptions = base.GetStaticPlotOptions();
		staticPlotOptions.Add(new PlotOption("Plot raster file outlines"));
		staticPlotOptions.Add(new PlotOption("Plot raster file names"));
		staticPlotOptions.Add(new PlotOption("Plot tile outlines"));
		staticPlotOptions.Add(new PlotOption("Plot cell outlines (when zoomed in)"));
		staticPlotOptions.Add(new PlotOption("Plot cell values (when zoomed in)"));
		staticPlotOptions.Add(new PlotOption("Plot stitch TIN edges"));
		staticPlotOptions.Add(new PlotOption("Plot Level0 stitch TIN edges"));
		staticPlotOptions.Add(new PlotOption("Remove Stitch Rendering"));
		return staticPlotOptions;
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlNodeList subLayerList = Layer.GetSubLayerList(xmlLayer);
		if (subLayerList.Count > 0)
		{
			bool condition = false;
			foreach (XmlElement subLayerList2 in subLayerList)
			{
				if (Operators.CompareString(subLayerList2.Attributes.Item(0).Value, "Modifications", TextCompare: false) == 0)
				{
					condition = true;
				}
			}
			if (!condition)
			{
				Nodes.Remove(_modificationGroup);
			}
			else if (!Nodes.Contains(_modificationGroup))
			{
				Nodes.Add(_modificationGroup);
			}
		}
		else
		{
			Nodes.Remove(_modificationGroup);
		}
	}

	protected override bool XMLLoadChild(XmlElement xmlEl)
	{
		string x = Layer.GetTypeNameFromXML(xmlEl).Trim();
		if (x.RoughlyEquals("ElevationModificationGroup") || x.RoughlyEquals("ElevationModificationGroup"))
		{
			_modificationGroup.XMLLoad(xmlEl);
			if (_modificationGroup.AllModifiers().Count() == 0)
			{
				Nodes.Remove(_modificationGroup);
			}
		}
		return true;
	}

	public RasterFileInfo RasterFileInfo(int rastIndex)
	{
		return RasterFilesInfo[rastIndex];
	}

	private void ReadRasterFilesInfo()
	{
		if (string.IsNullOrEmpty(SourceFilename))
		{
			_rasterFilesInfo = new RasterFileInfo[0];
			return;
		}
		if (!File.Exists(SourceFilename))
		{
			ErrorMessage = "Terrain file: '" + SourceFilename + "' not found.";
			_rasterFilesInfo = new RasterFileInfo[0];
			return;
		}
		checked
		{
			using H5Reader h5Reader = new H5Reader(SourceFilename);
			try
			{
				_hasLoadedRasterFiles = true;
				_rasterFilesExtent = null;
				string directoryName = System.IO.Path.GetDirectoryName(SourceFilename);
				List<string> groupNames = h5Reader.GetGroupNames("Terrain");
				_rasterFilesInfo = new RasterFileInfo[groupNames.Count - 1 + 1];
				string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(SourceFilename);
				bool condition = true;
				int count = groupNames.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					_rasterFilesInfo[i] = new RasterFileInfo(h5Reader, directoryName, fileNameWithoutExtension, groupNames[i]);
					if (_rasterFilesInfo[i].Priority == -1)
					{
						condition = false;
					}
					_rasterFilesExtent = Extent.Union(_rasterFilesExtent, _rasterFilesInfo[i].Extent);
				}
				if (condition)
				{
					_rasterFilesInfo = _rasterFilesInfo.OrderBy([SpecialName] (RasterFileInfo rfi) => rfi.Priority).ToArray();
				}
				if (!File.Exists(VRTFilename) && groupNames.Count > 0)
				{
					string[] strings = new string[groupNames.Count - 1 + 1];
					int count2 = groupNames.Count - 1;
					for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
					{
						strings[intervalIndex] = _rasterFilesInfo[intervalIndex].Filename;
					}
					InterpolatedLayer.CreateVRT(strings, VRTFilename, null);
				}
				if (File.Exists(VRTFilename))
				{
					_histogram = InterpolatedLayer.GetVRTHistogram(VRTFilename, null);
				}
				else
				{
					_histogram = Histogram.NullHistogram();
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				_rasterFilesInfo = new RasterFileInfo[0];
				string errorMessage = "Error reading terrain file: '" + SourceFilename + "'";
				if (SharedData.DebugMode)
				{
					errorMessage = errorMessage + "\r" + ex2.Message + "\r" + ex2.StackTrace;
				}
				ErrorMessage = errorMessage;
				ProjectData.ClearProjectError();
			}
		}
	}

	public void RasterFilePlainTile(int fileIndex, int level, int tileIndex, RasterM tileRaster)
	{
		RasterFileInfo rasterFileInfo = RasterFilesInfo[fileIndex];
		rasterFileInfo.TileRasters[level].SetCellTileRasterM(tileIndex, rasterFileInfo.TileSize[level], tileRaster);
	}

	public RasterM RasterFilePlainTile(int fileIndex, int level, int tileIndex)
	{
		RasterFileInfo rasterFileInfo = RasterFilesInfo[fileIndex];
		RasterM rasterM = new RasterM();
		rasterFileInfo.TileRasters[level].SetCellTileRasterM(tileIndex, rasterFileInfo.TileSize[level], rasterM);
		return rasterM;
	}

	public void RasterFileRenderTile(int fileIndex, int level, int tileIndex, RasterM tileRaster)
	{
		RasterFileInfo rasterFileInfo = RasterFilesInfo[fileIndex];
		rasterFileInfo.RenderBlockTiles[level].SetCellTileRasterM(tileIndex, rasterFileInfo.TileSize[level], tileRaster);
	}

	public void GetStatistics(ref double min, ref double max, ref double mean, ref double stdDev)
	{
		string vRTFilename = VRTFilename;
		if (!File.Exists(vRTFilename))
		{
			min = double.NaN;
			max = double.NaN;
			mean = double.NaN;
			stdDev = double.NaN;
		}
		else
		{
			GDALRaster.GetStats(vRTFilename, out min, out max, out mean, out stdDev);
		}
	}

	public void ComputeGenericFromTop(int targetDirFromTop, Action<RasterM, float[]> perTileAction, List<Extent> mustOverlapWith = null, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int rasterFilesInfoTerm = RasterFilesInfo.Count();
		checked
		{
			int[] intValues = new int[rasterFilesInfoTerm - 1 + 1];
			int count = rasterFilesInfoTerm - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i] = System.Math.Max(RasterFilesInfo[i].Levels - targetDirFromTop - 1, 0);
			}
			ComputeGeneric(perTileAction, mustOverlapWith, intValues, reporter);
		}
	}

	public void ComputeGenericFromBottom(int targetDirFromBottom, Action<RasterM, float[]> perTileAction, List<Extent> mustOverlapWith = null, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		int rasterFilesInfoTerm = RasterFilesInfo.Count();
		checked
		{
			int[] intValues = new int[rasterFilesInfoTerm - 1 + 1];
			int count = rasterFilesInfoTerm - 1;
			for (int i = 0; i <= count; i++)
			{
				intValues[i] = System.Math.Min(targetDirFromBottom, RasterFilesInfo[i].Levels - 1);
			}
			ComputeGeneric(perTileAction, mustOverlapWith, intValues, reporter);
		}
	}

	public void ComputeGeneric(Action<RasterM, float[]> perTileAction, List<Extent> mustOverlapWith = null, int[] targetDirectoryLevels = null, ProgressReporter reporter = null)
	{
		_Closure_0024__98_002D1 capturedLocals = default;
		_Closure_0024__98_002D1 capturedLocals2 = new _Closure_0024__98_002D1(capturedLocals);
		Stopwatch stopwatch = Stopwatch.StartNew();
		int rasterFilesInfoTerm = RasterFilesInfo.Count();
		capturedLocals2.tileRasterM = new RasterM();
		float[] arr = null;
		checked
		{
			int[] intValues = new int[rasterFilesInfoTerm - 1 + 1];
			int[] intValuesArray = new int[rasterFilesInfoTerm - 1 + 1];
			int count = rasterFilesInfoTerm - 1;
			for (int i = 0; i <= count; i++)
			{
				if (targetDirectoryLevels == null || targetDirectoryLevels.Count() != rasterFilesInfoTerm)
				{
					intValues[i] = System.Math.Min(0, RasterFilesInfo[i].Levels - 1);
				}
				else
				{
					intValues[i] = System.Math.Min(targetDirectoryLevels[i], RasterFilesInfo[i].Levels - 1);
				}
				intValuesArray[i] = RasterFilesInfo[i].RenderBlockTiles[intValues[i]].CellCount;
			}
			int sum = intValuesArray.Sum();
			float computed = 0f;
			float[] floatValues = new float[rasterFilesInfoTerm - 1 + 1];
			int count2 = rasterFilesInfoTerm - 1;
			for (int j = 0; j <= count2; j++)
			{
				floatValues[j] = (float)((double)intValuesArray[j] / (double)sum);
			}
			int count3 = RasterFileCount - 1;
			_Closure_0024__98_002D0 capturedLocals3 = default;
			for (int k = 0; k <= count3; k++)
			{
				capturedLocals3 = new _Closure_0024__98_002D0(capturedLocals3);
				new List<int>();
				capturedLocals3.fileRasterM = this.get_RasterFileExtent(k);
				if (mustOverlapWith != null && !mustOverlapWith.Any(capturedLocals3._Lambda_0024__0))
				{
					computed += 100f * floatValues[k];
					reporter.ReportProgress((int)System.Math.Round(computed));
					continue;
				}
				using FloatTiffReader floatTiffReader = new FloatTiffReader(this.get_RasterFilename(k));
				int ordinal = 0;
				int ordinal2 = intValuesArray[k];
				int ordinal3 = intValues[k];
				int count4 = ordinal2 - 1;
				for (int l = 0; l <= count4; l++)
				{
					RasterFileRenderTile(k, ordinal3, l, capturedLocals2.tileRasterM);
					if (mustOverlapWith == null || mustOverlapWith.Any([SpecialName] (Extent ext) => capturedLocals2.tileRasterM.Overlaps(ext)))
					{
						floatTiffReader.GetTile(ordinal3, l, ref arr);
						perTileAction(capturedLocals2.tileRasterM, arr);
					}
					ordinal++;
					if (unchecked(ordinal % 20) == 0)
					{
						float computed2 = (float)((double)ordinal / (double)intValuesArray[k]);
						reporter.ReportProgress((int)System.Math.Round(computed + 100f * computed2 * floatValues[k]));
					}
				}
				computed += 100f * floatValues[k];
				reporter.ReportProgress((int)System.Math.Round(computed));
			}
			stopwatch.Stop();
		}
	}

	private void ForceLoadObstructions()
	{
		_modificationGroup.ForceLoad();
	}

	private void ObstructProfiles(List<Polyline> plines, Profile[] profiles, float filterTol)
	{
		_modificationGroup.ObstructProfiles(plines, profiles);
	}

	private void ObstructTile(RasterM tileRaster, float[] values, float[,] perim)
	{
		ObstructRaster(tileRaster, values);
		ObstructPerimeter(tileRaster, perim);
	}

	private void ObstructProfile(H5Reader hr, double cellsize, List<Polyline> polylines, Profile[] profiles, float filterTol)
	{
		checked
		{
			int count = polylines.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Polyline polyline = polylines[i];
				Profile profile = profiles[i];
				polyline.ComputeExtent();
				if (profile == null)
				{
					profile = new Profile(DataUnits, DataUnits);
					profiles[i] = profile;
				}
			}
			throw new NotImplementedException();
		}
	}

	private void ObstructRaster(RasterM raster, float[] values)
	{
		_modificationGroup.ObstructRaster(raster, values);
	}

	private void ObstructPerimeter(RasterM tileRaster, float[,] perim)
	{
		checked
		{
			float[] floatValues = new float[tileRaster.Rows - 1 + 1];
			float[] floatValuesArray = new float[tileRaster.Cols + 1];
			Buffer.BlockCopy(perim, 0, floatValues, 0, floatValues.Count() * 4);
			Buffer.BlockCopy(perim, floatValues.Count() * 4, floatValuesArray, 0, floatValuesArray.Count() * 4);
			RasterM raster = new RasterM(tileRaster.Rows, 1, tileRaster.MaxX + tileRaster.CellSize, tileRaster.MaxX, tileRaster.MaxY, tileRaster.MinY);
			RasterM raster2 = new RasterM(1, tileRaster.Cols + 1, tileRaster.MinX, tileRaster.MaxX + tileRaster.CellSize, tileRaster.MinY, tileRaster.MinY - tileRaster.CellSize);
			_modificationGroup.ObstructRaster(raster, floatValues);
			_modificationGroup.ObstructRaster(raster2, floatValuesArray);
			Buffer.BlockCopy(floatValues, 0, perim, 0, floatValues.Count() * 4);
			Buffer.BlockCopy(floatValuesArray, 0, perim, floatValues.Count() * 4, floatValuesArray.Count() * 4);
		}
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		return ResampleInternal(raster, ref values, ref _stitches, ref _stitchTIN);
	}

	public bool Resample(RasterM raster, ref float[] values, int overrideLevel)
	{
		return ResampleInternal(raster, ref values, ref _stitches, ref _stitchTIN, 0, resampleWithStitches: true, null, overrideLevel);
	}

	public void Resample(RasterM raster, ref float[] values, bool useTerrainLayerStitches, Dictionary<string, TiffFile> tiffFiles = null, int overrideLevel = -1)
	{
		if (useTerrainLayerStitches)
		{
			ResampleInternal(raster, ref values, ref _stitches, ref _stitchTIN, 0, resampleWithStitches: true, tiffFiles, overrideLevel);
			return;
		}
		List<SegmentM> stitches = null;
		Tin stitchTIN = null;
		ResampleInternal(raster, ref values, ref stitches, ref stitchTIN, 0, resampleWithStitches: true, tiffFiles, overrideLevel);
	}

	public void Resample(RasterM raster, ref float[] values, ref List<SegmentM> stitches, string hdf5FileName, Dictionary<string, TiffFile> readers)
	{
		Buffers.ArraySetAllVB(ref values, -9999f, checked(raster.Cols * raster.Rows));
		if (stitches == null)
		{
			stitches = new List<SegmentM>();
		}
		else
		{
			stitches.Clear();
		}
		RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
		foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
		{
			if (raster.Overlaps(rasterFileInfo.Extent))
			{
				TiffFile tiffFile = readers[rasterFileInfo.Filename];
				if (tiffFile == null)
				{
					throw new ArgumentException("TerrainLayer.Resample: Reader " + rasterFileInfo.Filename + " does not exist.");
				}
				ResampleInternal(raster, ref values, ref stitches, rasterFileInfo, tiffFile, hdf5FileName);
			}
		}
		if (stitches.Count > 1)
		{
			new DelaunayTINFactory(0.01).ComputeTIN(null, null, null, new StitchCache(stitches))?.Rasterize(raster, values);
		}
		ObstructRaster(raster, values);
	}

	private bool ResampleInternal(RasterM raster, ref float[] values, [Optional][DefaultParameterValue(null)] ref List<SegmentM> stitches, [Optional][DefaultParameterValue(null)] ref Tin stitchTIN, int startRasterPriority = 0, bool resampleWithStitches = true, Dictionary<string, TiffFile> tiffFiles = null, int overrideLevel = -1)
	{
		if (!File.Exists(SourceFilename))
		{
			return false;
		}
		checked
		{
			Buffers.ArraySetAllVB(ref values, -9999f, raster.Cols * raster.Rows);
			if (stitches == null)
			{
				stitches = new List<SegmentM>();
			}
			else
			{
				stitches.Clear();
			}
			int count = RasterFilesInfo.Count() - 1;
			for (int i = startRasterPriority; i <= count; i++)
			{
				RasterFileInfo rasterFileInfo = RasterFilesInfo[i];
				if (!File.Exists(rasterFileInfo.Filename))
				{
					ErrorMessage = "Error resampling terrain. File '" + rasterFileInfo.Filename + "' does not exist.";
				}
				else
				{
					if (!raster.Overlaps(rasterFileInfo.Extent))
					{
						continue;
					}
					if (tiffFiles == null)
					{
						using TiffFile tr = new TiffFile(rasterFileInfo.Filename);
						ResampleInternal(raster, ref values, ref stitches, rasterFileInfo, tr, SourceFilename, overrideLevel);
					}
					else if (tiffFiles.ContainsKey(rasterFileInfo.Filename))
					{
						ResampleInternal(raster, ref values, ref stitches, rasterFileInfo, tiffFiles[rasterFileInfo.Filename], SourceFilename, overrideLevel);
					}
				}
			}
			if (stitches.Count > 0 && resampleWithStitches)
			{
				double maxValue = double.MaxValue;
				foreach (SegmentM stitch in stitches)
				{
					maxValue = System.Math.Min(maxValue, stitch.A.M);
				}
				double systemMaxValueTerm = 0.01;
				systemMaxValueTerm = System.Math.Min(systemMaxValueTerm, maxValue * 0.1);
				Extent extent = new Extent(stitches.Cast<IExtent>().ToArray());
				extent = extent.RatioAboutCenter(1.5);
				bool removeDeletedTriangles = false;
				DevelopmentTin developmentTin = new DelaunayTINFactory(systemMaxValueTerm).ComputeTIN(null, null, null, new StitchCache(stitches), removeDeletedTriangles, double.NaN, null, null, extent);
				if (developmentTin == null)
				{
					stitchTIN = null;
				}
				else
				{
					stitchTIN = developmentTin.MakeCleanTin();
					if (stitchTIN != null)
					{
						stitchTIN.Rasterize(raster, values);
					}
				}
			}
			else
			{
				stitchTIN = null;
			}
			ObstructRaster(raster, values);
			return true;
		}
	}

	public bool TileHasData(int rasteridx, int pLevel, int tileIdx)
	{
		return RasterFilesInfo[rasteridx].TileHasAnyData[pLevel][tileIdx];
	}

	public bool ReadMaskedTile(int rasterIdx, int pyramidLevel, int tileIdx, ref float[] values, H5Reader hr, FloatTiffReader tr, object H5Lock = null, object FTRLock = null)
	{
		if (pyramidLevel != 0)
		{
			throw new NotImplementedException();
		}
		RasterFileInfo rasterFileInfo = RasterFilesInfo[rasterIdx];
		checked
		{
			int tileSizeRasterFileInfoTerm = rasterFileInfo.TileSize[pyramidLevel] * rasterFileInfo.TileSize[pyramidLevel];
			if (!rasterFileInfo.TileHasAnyData[pyramidLevel][tileIdx])
			{
				return false;
			}
			if (values == null || values.Length != tileSizeRasterFileInfoTerm)
			{
				values = new float[tileSizeRasterFileInfoTerm - 1 + 1];
			}
			ReadMaskedTileInternal(rasterFileInfo, pyramidLevel, tileIdx, ref values, hr, tr, RuntimeHelpers.GetObjectValue(H5Lock), RuntimeHelpers.GetObjectValue(FTRLock));
			return true;
		}
	}

	public bool ReadMaskedTileLocked(int rasterIdx, int pyramidLevel, int tileIdx, ref float[] values, H5Reader hr, FloatTiffReader tr, object H5Lock, object FTRLock)
	{
		if (pyramidLevel != 0)
		{
			throw new NotImplementedException();
		}
		RasterFileInfo rasterFileInfo = RasterFilesInfo[rasterIdx];
		checked
		{
			int tileSizeRasterFileInfoTerm = rasterFileInfo.TileSize[pyramidLevel] * rasterFileInfo.TileSize[pyramidLevel];
			if (!rasterFileInfo.TileHasAnyData[pyramidLevel][tileIdx])
			{
				return false;
			}
			if (values == null || values.Length != tileSizeRasterFileInfoTerm)
			{
				values = new float[tileSizeRasterFileInfoTerm - 1 + 1];
			}
			ReadMaskedTileInternalLocked(rasterFileInfo, pyramidLevel, tileIdx, ref values, hr, tr, RuntimeHelpers.GetObjectValue(H5Lock), RuntimeHelpers.GetObjectValue(FTRLock));
			throw new NotImplementedException();
		}
	}

	private void ResampleInternal(RasterM raster, ref float[] values, ref List<SegmentM> stitches, RasterFileInfo rfi, TiffFile tr, string hdf5FileName, int overrideLevel = -1)
	{
		_Closure_0024__113_002D0 capturedLocals = default;
		_Closure_0024__113_002D0 capturedLocals2 = new _Closure_0024__113_002D0(capturedLocals);
		capturedLocals2.terrainLayer = this;
		capturedLocals2.raster = raster;
		capturedLocals2.rfi = rfi;
		capturedLocals2.tr = tr;
		RasterM rasterM = new RasterM();
		if (overrideLevel == -1)
		{
			capturedLocals2.level = capturedLocals2.rfi.ComputeLevel(capturedLocals2.raster.CellSize);
		}
		else
		{
			capturedLocals2.level = overrideLevel;
		}
		capturedLocals2.tileRaster = capturedLocals2.rfi.RenderBlockTiles[capturedLocals2.level];
		RowColRangeM rowColRangeM = capturedLocals2.raster.RowColRange(capturedLocals2.rfi.Extent, limitToRaster: true);
		RasMapperLib.Terrain.TileColumn tileColumn = null;
		capturedLocals2.tileColumns = new List<RasMapperLib.Terrain.TileColumn>();
		RasMapperLib.Terrain.RenderBlockColumn renderBlockColumn = null;
		int minCol = rowColRangeM.MinCol;
		int maxCol = rowColRangeM.MaxCol;
		checked
		{
			int c = default;
			float frac = default;
			for (int i = minCol; i <= maxCol; i++)
			{
				double x = capturedLocals2.raster.CellCenterX(i);
				int col = capturedLocals2.tileRaster.Col(x);
				if (col >= 0 && col < capturedLocals2.tileRaster.Cols)
				{
					if (tileColumn == null || tileColumn.Index != col)
					{
						tileColumn = new RasMapperLib.Terrain.TileColumn(col);
						capturedLocals2.tileColumns.Add(tileColumn);
						capturedLocals2.tileRaster.SetCellTileRasterM(0, col, capturedLocals2.rfi.TileSize[capturedLocals2.level], rasterM);
						renderBlockColumn = null;
					}
					rasterM.ColAndFraction(x, ref c, ref frac);
					if (renderBlockColumn == null || renderBlockColumn.index != c)
					{
						renderBlockColumn = new RasMapperLib.Terrain.RenderBlockColumn(c);
						tileColumn.RenderBlockColumns.Add(renderBlockColumn);
					}
					renderBlockColumn.RasterColumns.Add(new RasMapperLib.Terrain.RasterColumn(i, frac));
				}
			}
			RasMapperLib.Terrain.TileRow tileRow = null;
			List<RasMapperLib.Terrain.TileRow> tileRows = new List<RasMapperLib.Terrain.TileRow>();
			RasMapperLib.Terrain.RenderBlockRow renderBlockRow = null;
			int minRow = rowColRangeM.MinRow;
			int maxRow = rowColRangeM.MaxRow;
			int r = default;
			float frac2 = default;
			for (int j = minRow; j <= maxRow; j++)
			{
				double y = capturedLocals2.raster.CellCenterY(j);
				int row = capturedLocals2.tileRaster.Row(y);
				if (row >= 0 && row < capturedLocals2.tileRaster.Rows)
				{
					if (tileRow == null || tileRow.Index != row)
					{
						tileRow = new RasMapperLib.Terrain.TileRow(row);
						tileRows.Add(tileRow);
						capturedLocals2.tileRaster.SetCellTileRasterM(row, 0, capturedLocals2.rfi.TileSize[capturedLocals2.level], rasterM);
						renderBlockRow = null;
					}
					rasterM.RowAndFraction(y, ref r, ref frac2);
					if (renderBlockRow == null || renderBlockRow.index != r)
					{
						renderBlockRow = new RasMapperLib.Terrain.RenderBlockRow(r);
						tileRow.RenderBlockRows.Add(renderBlockRow);
					}
					renderBlockRow.RasterRows.Add(new RasMapperLib.Terrain.RasterRow(j, frac2));
				}
			}
			if (tileRows.Count == 0 || capturedLocals2.tileColumns.Count == 0)
			{
				return;
			}
			BufferRenderBlockData(capturedLocals2.tileRaster, capturedLocals2.tileColumns, tileRows, capturedLocals2.rfi.TileSize[capturedLocals2.level], 2);
			RuntimeHelpers.GetObjectValue(new object());
			capturedLocals2.stitchLock = RuntimeHelpers.GetObjectValue(new object());
			capturedLocals2.byvalValues = values;
			capturedLocals2.byvalStitches = new List<SegmentM>();
			if (_hdf5PerimeterRowCache == null)
			{
				_hdf5PerimeterRowCache = _hdf5Cache.GetRowCache<float>(hdf5FileName);
			}
			if (_hdf5MaskRowCache == null)
			{
				_hdf5MaskRowCache = _hdf5Cache.GetRowCache<byte>(hdf5FileName);
			}
			capturedLocals2.h5lock = RuntimeHelpers.GetObjectValue(new object());
			System.Threading.Tasks.Parallel.ForEach(tileRows, [SpecialName] (RasMapperLib.Terrain.TileRow tRow) =>
			{
				_Closure_0024__113_002D1 capturedLocals3 = new _Closure_0024__113_002D1(null);
				capturedLocals3.outerClosure = capturedLocals2;
				capturedLocals3.tRow = tRow;
				capturedLocals3.localTile = new RasterM();
				List<SegmentM> segmentMs = new List<SegmentM>();
				int index = capturedLocals3.tRow.Index;
				using (List<RasMapperLib.Terrain.TileColumn>.Enumerator tileColumns = capturedLocals2.tileColumns.GetEnumerator())
				{
					_Closure_0024__113_002D4 capturedLocals4 = default;
					_Closure_0024__113_002D2 capturedLocals5 = default;
					_Closure_0024__113_002D3 capturedLocals6 = default;
					while (tileColumns.MoveNext())
					{
						capturedLocals4 = new _Closure_0024__113_002D4(capturedLocals4);
						capturedLocals4.outerClosure = capturedLocals3;
						capturedLocals4.tCol = tileColumns.Current;
						capturedLocals5 = new _Closure_0024__113_002D2(capturedLocals5);
						capturedLocals5.outerClosure = capturedLocals4;
						int index2 = capturedLocals5.outerClosure.tCol.Index;
						int cellIndex = capturedLocals2.tileRaster.CellIndex(index, index2);
						if (capturedLocals2.rfi.TileHasAnyData[capturedLocals2.level][cellIndex])
						{
							capturedLocals2.tileRaster.SetCellTileRasterM(cellIndex, capturedLocals2.rfi.TileSize[capturedLocals2.level], capturedLocals5.outerClosure.outerClosure.localTile);
							TileDescriptor key = new TileDescriptor(capturedLocals2.rfi.Filename, capturedLocals2.level, cellIndex);
							if (capturedLocals2.terrainLayer._tileCacheConcurrent.ContainsKey(key))
							{
								capturedLocals5.tileData = capturedLocals2.terrainLayer._tileCacheConcurrent[key];
							}
							else
							{
								capturedLocals5.tileData = new TileData();
								capturedLocals5.tileData.tileValues = capturedLocals2.tr.Directories[capturedLocals2.level].GetTileVB<float>(cellIndex);
								object h5lock = capturedLocals2.h5lock;
								ObjectFlowControl.CheckForSyncLockOnValueType(h5lock);
								bool lockTaken = false;
								try
								{
									Monitor.Enter(h5lock, ref lockTaken);
									capturedLocals5.tileData.perimeterValues = capturedLocals2.terrainLayer._hdf5PerimeterRowCache.EnsureGetRow(capturedLocals2.rfi.Groupname + "/" + Strings.Format(capturedLocals2.level) + "/Perimeter", cellIndex);
									capturedLocals5.tileData.mask = capturedLocals2.terrainLayer._hdf5MaskRowCache.EnsureGetRow(capturedLocals2.rfi.Groupname + "/" + Strings.Format(capturedLocals2.level) + "/Mask", cellIndex);
								}
								finally
								{
									if (lockTaken)
									{
										Monitor.Exit(h5lock);
									}
								}
							}
							if (capturedLocals2.terrainLayer._tileCacheConcurrent.TryAdd(key, capturedLocals5.tileData))
							{
								capturedLocals5.tileData.TimeLastUsed = Interlocked.Increment(ref capturedLocals2.terrainLayer._tileCount) - 1;
							}
							else
							{
								capturedLocals5.tileData.TimeLastUsed = capturedLocals2.terrainLayer._tileCount;
							}
							int count = capturedLocals2.terrainLayer._tileCacheConcurrent.Count;
							if (count > 50)
							{
								capturedLocals2.terrainLayer.ClearLRUTilesConcurrent(5, count);
							}
							capturedLocals6 = new _Closure_0024__113_002D3(capturedLocals6);
							capturedLocals6.outerClosure = capturedLocals5;
							capturedLocals6.threadLocalRenderBlockGetter = new ThreadLocal<RasMapperLib.Terrain.RenderBlock>([SpecialName] () =>
							{
								RasMapperLib.Terrain.RenderBlock renderBlock = new RasMapperLib.Terrain.RenderBlock();
								renderBlock.SetTile(capturedLocals6.outerClosure.outerClosure.outerClosure.localTile, capturedLocals6.outerClosure.tileData.tileValues, capturedLocals6.outerClosure.tileData.perimeterValues, capturedLocals6.outerClosure.tileData.mask, new List<SegmentM>());
								return renderBlock;
							}, trackAllValues: true);
							System.Threading.Tasks.Parallel.For(0, capturedLocals6.outerClosure.outerClosure.outerClosure.tRow.RenderBlockRows.Count, capturedLocals6._Lambda_0024__2);
							foreach (RasMapperLib.Terrain.RenderBlock renderBlock2 in capturedLocals6.threadLocalRenderBlockGetter.Values)
							{
								segmentMs.AddRange(renderBlock2.Stitches);
							}
							capturedLocals6.threadLocalRenderBlockGetter.Dispose();
						}
					}
				}
				object stitchLock = capturedLocals2.stitchLock;
				ObjectFlowControl.CheckForSyncLockOnValueType(stitchLock);
				bool lockTaken2 = false;
				try
				{
					Monitor.Enter(stitchLock, ref lockTaken2);
					capturedLocals2.byvalStitches.AddRange(segmentMs);
				}
				finally
				{
					if (lockTaken2)
					{
						Monitor.Exit(stitchLock);
					}
				}
			});
			stitches.AddRange(capturedLocals2.byvalStitches.OrderBy([SpecialName] (SegmentM seg) => seg.GetHashCode()));
		}
	}

	private void BufferRenderBlockData(RasterM tileRaster, List<RasMapperLib.Terrain.TileColumn> tileColumns, List<RasMapperLib.Terrain.TileRow> tileRows, int tileSize, int bufferRadius)
	{
		RasMapperLib.Terrain.TileColumn tileColumn = tileColumns.First();
		checked
		{
			int count = bufferRadius - 1;
			for (int i = 0; i <= count; i++)
			{
				if (tileColumn.RenderBlockColumns.Count == 0)
				{
					break;
				}
				RasMapperLib.Terrain.RenderBlockColumn renderBlockColumn = tileColumn.RenderBlockColumns.First();
				if (renderBlockColumn.index == 0)
				{
					int indexOffset = tileColumn.Index - 1;
					if (indexOffset < 0)
					{
						break;
					}
					RasMapperLib.Terrain.TileColumn tileColumn2 = new RasMapperLib.Terrain.TileColumn(indexOffset);
					tileColumn2.RenderBlockColumns.Add(new RasMapperLib.Terrain.RenderBlockColumn(tileSize - 1));
					tileColumns.Insert(0, tileColumn2);
					tileColumn = tileColumn2;
				}
				else
				{
					tileColumn.RenderBlockColumns.Insert(0, new RasMapperLib.Terrain.RenderBlockColumn(renderBlockColumn.index - 1));
				}
			}
			RasMapperLib.Terrain.TileColumn tileColumn3 = tileColumns.Last();
			int count2 = bufferRadius - 1;
			for (int j = 0; j <= count2; j++)
			{
				if (tileColumn3.RenderBlockColumns.Count == 0)
				{
					break;
				}
				RasMapperLib.Terrain.RenderBlockColumn renderBlockColumn2 = tileColumn3.RenderBlockColumns.Last();
				if (renderBlockColumn2.index == tileSize - 1)
				{
					int indexOffset2 = tileColumn3.Index + 1;
					if (indexOffset2 >= tileRaster.Cols)
					{
						break;
					}
					RasMapperLib.Terrain.TileColumn tileColumn4 = new RasMapperLib.Terrain.TileColumn(indexOffset2);
					tileColumn4.RenderBlockColumns.Add(new RasMapperLib.Terrain.RenderBlockColumn(0));
					tileColumns.Add(tileColumn4);
					tileColumn3 = tileColumn4;
				}
				else
				{
					tileColumn3.RenderBlockColumns.Add(new RasMapperLib.Terrain.RenderBlockColumn(renderBlockColumn2.index + 1));
				}
			}
			RasMapperLib.Terrain.TileRow tileRow = tileRows.First();
			int count3 = bufferRadius - 1;
			for (int k = 0; k <= count3; k++)
			{
				if (tileRow.RenderBlockRows.Count == 0)
				{
					break;
				}
				RasMapperLib.Terrain.RenderBlockRow renderBlockRow = tileRow.RenderBlockRows.First();
				if (renderBlockRow.index == 0)
				{
					int indexOffset3 = tileRow.Index - 1;
					if (indexOffset3 < 0)
					{
						break;
					}
					RasMapperLib.Terrain.TileRow tileRow2 = new RasMapperLib.Terrain.TileRow(indexOffset3);
					tileRow2.RenderBlockRows.Add(new RasMapperLib.Terrain.RenderBlockRow(tileSize - 1));
					tileRows.Insert(0, tileRow2);
					tileRow = tileRow2;
				}
				else
				{
					tileRow.RenderBlockRows.Insert(0, new RasMapperLib.Terrain.RenderBlockRow(renderBlockRow.index - 1));
				}
			}
			RasMapperLib.Terrain.TileRow tileRow3 = tileRows.Last();
			int count4 = bufferRadius - 1;
			for (int l = 0; l <= count4; l++)
			{
				if (tileRow3.RenderBlockRows.Count == 0)
				{
					break;
				}
				RasMapperLib.Terrain.RenderBlockRow renderBlockRow2 = tileRow3.RenderBlockRows.Last();
				if (renderBlockRow2.index == tileSize - 1)
				{
					int indexOffset4 = tileRow3.Index + 1;
					if (indexOffset4 >= tileRaster.Rows)
					{
						break;
					}
					RasMapperLib.Terrain.TileRow tileRow4 = new RasMapperLib.Terrain.TileRow(indexOffset4);
					tileRow4.RenderBlockRows.Add(new RasMapperLib.Terrain.RenderBlockRow(0));
					tileRows.Add(tileRow4);
					tileRow3 = tileRow4;
				}
				else
				{
					tileRow3.RenderBlockRows.Add(new RasMapperLib.Terrain.RenderBlockRow(renderBlockRow2.index + 1));
				}
			}
		}
	}

	private void ReadMaskedTileInternalLocked(RasterFileInfo rfi, int pyramidLevel, int tileIdx, ref float[] values, H5Reader hr, FloatTiffReader ftr, object H5Lock, object FTRLock)
	{
		_Closure_0024__115_002D0 capturedLocals = default;
		_Closure_0024__115_002D0 capturedLocals2 = new _Closure_0024__115_002D0(capturedLocals);
		if (!_triedStitchReading)
		{
			throw new Exception("Must call Level0StitchTIN before this function");
		}
		ObjectFlowControl.CheckForSyncLockOnValueType(FTRLock);
		bool lockTaken = false;
		byte[] tileRaw;
		try
		{
			Monitor.Enter(FTRLock, ref lockTaken);
			tileRaw = ftr.GetTileRaw(pyramidLevel, tileIdx);
		}
		finally
		{
			if (lockTaken)
			{
				Monitor.Exit(FTRLock);
			}
		}
		new MemoryStream(tileRaw);
		int tileSize = rfi.TileSize[pyramidLevel];
		checked
		{
			int tileSizePolynomial = tileSize * tileSize;
			if (values == null || values.Length != tileSizePolynomial)
			{
				values = new float[tileSizePolynomial - 1 + 1];
			}
			capturedLocals2.rbMask = null;
			ObjectFlowControl.CheckForSyncLockOnValueType(H5Lock);
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(H5Lock, ref lockTaken2);
				hr.ReadRows(rfi.Groupname + "/" + Strings.Format(pyramidLevel) + "/Mask", tileIdx, 1, ref capturedLocals2.rbMask);
			}
			finally
			{
				if (lockTaken2)
				{
					Monitor.Exit(H5Lock);
				}
			}
			capturedLocals2.cellMask = new byte[capturedLocals2.rbMask.GetLength(1) - 1 + 1];
			Utility.Parallel.SmartFor(capturedLocals2.cellMask.Length, [SpecialName] (int start, int finish) =>
			{
				bool systemStartTerm = System.Math.Floor((double)start / 256.0) == 255.0;
				int count = finish - 1;
				for (int i = start; i <= count; i++)
				{
					if ((capturedLocals2.rbMask[0, i] & 1) == 1)
					{
						capturedLocals2.cellMask[i] = 1;
						capturedLocals2.cellMask[i + 1] = 1;
						if (!systemStartTerm)
						{
							capturedLocals2.cellMask[i + 256] = 1;
							capturedLocals2.cellMask[i + 257] = 1;
						}
					}
				}
				if ((capturedLocals2.rbMask[0, finish] & 1) == 1)
				{
					capturedLocals2.cellMask[finish] = 1;
					if (!systemStartTerm)
					{
						capturedLocals2.cellMask[finish + 256] = 1;
					}
				}
			}, 256);
			Tin tin = Level0StitchTIN();
			if (tin != null)
			{
				RasterM tileRaster = rfi.TileRasters[0];
				RasterM rasterM = new RasterM();
				tileRaster.SetCellTileRasterM(tileIdx, rfi.TileSize[0], rasterM);
				List<int> distinctElements = _stitchTINSpatialIndex.GetDistinctElements(rasterM.ComputeExtent());
				foreach (int distinctElement in distinctElements)
				{
					Extent extent = tin.TriangleExtent(distinctElement);
					RowColRangeM rowColRangeM = rasterM.RowColRange(extent, limitToRaster: true);
					int minRow = rowColRangeM.MinRow;
					int maxRow = rowColRangeM.MaxRow;
					for (int minRow2 = minRow; minRow2 <= maxRow; minRow2++)
					{
						int minCol = rowColRangeM.MinCol;
						int maxCol = rowColRangeM.MaxCol;
						for (int minCol2 = minCol; minCol2 <= maxCol; minCol2++)
						{
							int minRow2ColsTerm = minRow2 * rasterM.Cols + minCol2;
							capturedLocals2.cellMask[minRow2ColsTerm] = 2;
						}
					}
				}
			}
			capturedLocals2.lvals = values;
			Utility.Parallel.SmartFor(capturedLocals2.lvals.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals2.cellMask[i] == 0)
					{
						capturedLocals2.lvals[i] = -9999f;
					}
				}
			}, 1024);
			throw new NotImplementedException();
		}
	}

	private void ReadMaskedTileInternal(RasterFileInfo rfi, int pyramidLevel, int tileIdx, ref float[] values, H5Reader hr, FloatTiffReader ftr, object H5Lock = null, object FTRLock = null)
	{
		_Closure_0024__116_002D0 capturedLocals = default;
		_Closure_0024__116_002D0 capturedLocals2 = new _Closure_0024__116_002D0(capturedLocals);
		if (!_triedStitchReading)
		{
			throw new Exception("Must call Level0StitchTIN before this function");
		}
		if (FTRLock == null)
		{
			ftr.GetTile(pyramidLevel, tileIdx, ref values);
		}
		else
		{
			ObjectFlowControl.CheckForSyncLockOnValueType(FTRLock);
			bool lockTaken = false;
			try
			{
				Monitor.Enter(FTRLock, ref lockTaken);
				ftr.GetTile(pyramidLevel, tileIdx, ref values);
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(FTRLock);
				}
			}
		}
		capturedLocals2.rbMask = null;
		if (H5Lock == null)
		{
			hr.ReadRows(rfi.Groupname + "/" + Strings.Format(pyramidLevel) + "/Mask", tileIdx, 1, ref capturedLocals2.rbMask);
		}
		else
		{
			ObjectFlowControl.CheckForSyncLockOnValueType(H5Lock);
			bool lockTaken2 = false;
			try
			{
				Monitor.Enter(H5Lock, ref lockTaken2);
				hr.ReadRows(rfi.Groupname + "/" + Strings.Format(pyramidLevel) + "/Mask", tileIdx, 1, ref capturedLocals2.rbMask);
			}
			finally
			{
				if (lockTaken2)
				{
					Monitor.Exit(H5Lock);
				}
			}
		}
		checked
		{
			capturedLocals2.cellMask = new byte[capturedLocals2.rbMask.GetLength(1) - 1 + 1];
			Utility.Parallel.SmartFor(capturedLocals2.cellMask.Length, [SpecialName] (int start, int finish) =>
			{
				bool systemStartTerm = System.Math.Floor((double)start / 256.0) == 255.0;
				int count = finish - 1;
				for (int i = start; i <= count; i++)
				{
					if ((capturedLocals2.rbMask[0, i] & 1) == 1)
					{
						capturedLocals2.cellMask[i] = 1;
						capturedLocals2.cellMask[i + 1] = 1;
						if (!systemStartTerm)
						{
							capturedLocals2.cellMask[i + 256] = 1;
							capturedLocals2.cellMask[i + 257] = 1;
						}
					}
				}
				if ((capturedLocals2.rbMask[0, finish] & 1) == 1)
				{
					capturedLocals2.cellMask[finish] = 1;
					if (!systemStartTerm)
					{
						capturedLocals2.cellMask[finish + 256] = 1;
					}
				}
			}, 256);
			Tin tin = Level0StitchTIN();
			RasterM tileRaster = rfi.TileRasters[0];
			RasterM rasterM = new RasterM();
			tileRaster.SetCellTileRasterM(tileIdx, rfi.TileSize[0], rasterM);
			if (tin != null)
			{
				List<int> distinctElements = _stitchTINSpatialIndex.GetDistinctElements(rasterM.ComputeExtent());
				foreach (int distinctElement in distinctElements)
				{
					Extent extent = tin.TriangleExtent(distinctElement);
					RowColRangeM rowColRangeM = rasterM.RowColRange(extent, limitToRaster: true);
					int minRow = rowColRangeM.MinRow;
					int maxRow = rowColRangeM.MaxRow;
					for (int minRow2 = minRow; minRow2 <= maxRow; minRow2++)
					{
						int minCol = rowColRangeM.MinCol;
						int maxCol = rowColRangeM.MaxCol;
						for (int minCol2 = minCol; minCol2 <= maxCol; minCol2++)
						{
							int minRow2ColsTerm = minRow2 * rasterM.Cols + minCol2;
							capturedLocals2.cellMask[minRow2ColsTerm] = 2;
						}
					}
				}
			}
			ObstructRaster(rasterM, values);
			capturedLocals2.lvals = values;
			Utility.Parallel.SmartFor(capturedLocals2.lvals.Count(), [SpecialName] (int start, int finish) =>
			{
				for (int i = start; i <= finish; i++)
				{
					if (capturedLocals2.cellMask[i] == 0)
					{
						capturedLocals2.lvals[i] = -9999f;
					}
				}
			}, 1024);
		}
	}

	public float[] GetMinZMaxZNoDataUsingRaster(RasterM raster)
	{
		float[] minZMaxZNoDataUsingRaster = new float[3]
		{
			float.MaxValue,
			float.MinValue,
			1f
		};
		List<float> floatList = new List<float>();
		List<float> floatListList = new List<float>();
		List<float> floatList2 = new List<float>();
		RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
		checked
		{
			int c = default;
			float frac = default;
			int r = default;
			float frac2 = default;
			foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
			{
				if (!raster.Overlaps(rasterFileInfo.Extent))
				{
					continue;
				}
				RasterM rasterM = new RasterM();
				int level = rasterFileInfo.ComputeLevel(raster.CellSize);
				RasterM rasterM2 = rasterFileInfo.RenderBlockTiles[level];
				RowColRangeM rowColRangeM = raster.RowColRange(rasterFileInfo.Extent, limitToRaster: true);
				RasMapperLib.Terrain.TileColumn tileColumn = null;
				List<RasMapperLib.Terrain.TileColumn> tileColumns = new List<RasMapperLib.Terrain.TileColumn>();
				RasMapperLib.Terrain.RenderBlockColumn renderBlockColumn = null;
				int minCol = rowColRangeM.MinCol;
				int maxCol = rowColRangeM.MaxCol;
				for (int j = minCol; j <= maxCol; j++)
				{
					double x = raster.CellCenterX(j);
					int col = rasterM2.Col(x);
					if (col >= 0 && col < rasterM2.Cols)
					{
						if (tileColumn == null || tileColumn.Index != col)
						{
							tileColumn = new RasMapperLib.Terrain.TileColumn(col);
							tileColumns.Add(tileColumn);
							rasterM2.SetCellTileRasterM(0, col, rasterFileInfo.TileSize[level], rasterM);
							renderBlockColumn = null;
						}
						rasterM.ColAndFraction(x, ref c, ref frac);
						if (renderBlockColumn == null || renderBlockColumn.index != c)
						{
							renderBlockColumn = new RasMapperLib.Terrain.RenderBlockColumn(c);
							tileColumn.RenderBlockColumns.Add(renderBlockColumn);
						}
						renderBlockColumn.RasterColumns.Add(new RasMapperLib.Terrain.RasterColumn(j, frac));
					}
				}
				RasMapperLib.Terrain.TileRow tileRow = null;
				List<RasMapperLib.Terrain.TileRow> tileRows = new List<RasMapperLib.Terrain.TileRow>();
				RasMapperLib.Terrain.RenderBlockRow renderBlockRow = null;
				int minRow = rowColRangeM.MinRow;
				int maxRow = rowColRangeM.MaxRow;
				for (int k = minRow; k <= maxRow; k++)
				{
					double y = raster.CellCenterY(k);
					int row = rasterM2.Row(y);
					if (row >= 0 && row < rasterM2.Rows)
					{
						if (tileRow == null || tileRow.Index != row)
						{
							tileRow = new RasMapperLib.Terrain.TileRow(row);
							tileRows.Add(tileRow);
							rasterM2.SetCellTileRasterM(row, 0, rasterFileInfo.TileSize[level], rasterM);
							renderBlockRow = null;
						}
						rasterM.RowAndFraction(y, ref r, ref frac2);
						if (renderBlockRow == null || renderBlockRow.index != r)
						{
							renderBlockRow = new RasMapperLib.Terrain.RenderBlockRow(r);
							tileRow.RenderBlockRows.Add(renderBlockRow);
						}
						renderBlockRow.RasterRows.Add(new RasMapperLib.Terrain.RasterRow(k, frac2));
					}
				}
				if (_hdf5MinMaxNoDataRowCache == null)
				{
					_hdf5MinMaxNoDataRowCache = _hdf5Cache.GetRowCache<float>(SourceFilename);
				}
				foreach (RasMapperLib.Terrain.TileRow tileRow2 in tileRows)
				{
					int index = tileRow2.Index;
					foreach (RasMapperLib.Terrain.TileColumn tileColumn2 in tileColumns)
					{
						int index2 = tileColumn2.Index;
						int cellIndex = rasterM2.CellIndex(index, index2);
						if (rasterFileInfo.TileHasAnyData[level][cellIndex])
						{
							float[] ensureGetRow = null;
							ensureGetRow = _hdf5MinMaxNoDataRowCache.EnsureGetRow(rasterFileInfo.Groupname + "/" + Strings.Format(level) + "/Min-Max", cellIndex);
							floatListList.Add(ensureGetRow[0]);
							floatList.Add(ensureGetRow[1]);
							floatList2.Add(ensureGetRow[2]);
						}
					}
				}
			}
			float maxValue = float.MaxValue;
			float minValue = float.MinValue;
			float computed = 0f;
			int count = floatListList.Count - 1;
			for (int l = 0; l <= count; l++)
			{
				if (floatListList[l] < maxValue)
				{
					maxValue = floatListList[l];
				}
				if (floatList[l] > minValue)
				{
					minValue = floatList[l];
				}
				computed += floatList2[l];
			}
			if (floatListList.Count > 0)
			{
				minZMaxZNoDataUsingRaster[0] = maxValue;
				minZMaxZNoDataUsingRaster[1] = minValue;
				minZMaxZNoDataUsingRaster[2] = computed / (float)floatList2.Count;
			}
			return minZMaxZNoDataUsingRaster;
		}
	}

	public void ClearTileCache()
	{
		ClearLRUTiles(int.MaxValue);
	}

	private void ClearLRUTiles(int count)
	{
		if (count > _tileCache.Count)
		{
			count = _tileCache.Count;
		}
		List<KeyValuePair<TileDescriptor, TileData>> take = _tileCache.OrderBy([SpecialName] (KeyValuePair<TileDescriptor, TileData> k) => k.Value.TimeLastUsed).Take(count).ToList();
		foreach (KeyValuePair<TileDescriptor, TileData> take2 in take)
		{
			_tileCache.Remove(take2.Key);
		}
	}

	private void ClearLRUTilesConcurrent(int count, int tileCacheCount)
	{
		if (count > tileCacheCount)
		{
			count = tileCacheCount;
		}
		List<KeyValuePair<TileDescriptor, TileData>> take = (from k in _tileCacheConcurrent.ToArray()
			orderby k.Value.TimeLastUsed
			select k).Take(count).ToList();
		foreach (KeyValuePair<TileDescriptor, TileData> take2 in take)
		{
			TileData tileData = null;
			_tileCacheConcurrent.TryRemove(take2.Key, out tileData);
		}
	}

	private float[,] ReadRasterMinMaxNoData(H5Reader hr, RasterFileInfo rfi, int level)
	{
		float[,] result;
		try
		{
			float[,] values = new float[1, 1];
			hr.ReadDataset(Geospatial.Terrain.Terrain.GetMinMaxName(rfi.Filename, level), ref values);
			result = values;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Error reading terrain information. Try remaking the terrain file.";
			result = null;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public float[] ComputePointElevations(PointM[] points, bool useObstructions = true)
	{
		if (points.Length == 0)
		{
			return new float[0];
		}
		PointMs pointMs = new PointMs(points);
		return ComputePointElevations(pointMs.ComputeExtent(), pointMs, useObstructions);
	}

	public float[] ComputePointElevations(IPoints points, bool useObstructions = true)
	{
		if (points.PointMCount() == 0)
		{
			return new float[0];
		}
		return ComputePointElevations(points.ComputeExtent(), points, useObstructions);
	}

	public float[] ComputePointElevations(Extent ext, IPoints points, bool useObstructions = true)
	{
		float[] pointElevations = new float[checked(points.PointMCount() - 1 + 1)];
		Buffers.ArraySetAllVB(pointElevations, -9999f);
		ComputePointElevations(ext, points, pointElevations, useObstructions);
		return pointElevations;
	}

	public void ComputePointElevations(Extent ext, IPoints points, float[] zPerPoint, bool useObstructions = true)
	{
		if (!ext.Overlaps(Extent))
		{
			return;
		}
		SpatialIndex<int> spatialIndex = new SpatialIndex<int>(ext);
		checked
		{
			int count = points.PointMCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				spatialIndex.Add(points.PointM(i), i);
			}
			RasterM rasterM = new RasterM();
			RasMapperLib.Terrain.RenderBlock renderBlock = new RasMapperLib.Terrain.RenderBlock();
			float[] tileVB = null;
			float[] ensureGetRow = null;
			byte[] ensureGetRowArray = null;
			List<SegmentM> stitches = new List<SegmentM>();
			if (_hdf5PerimeterRowCache == null)
			{
				_hdf5PerimeterRowCache = _hdf5Cache.GetRowCache<float>(SourceFilename);
			}
			if (_hdf5MaskRowCache == null)
			{
				_hdf5MaskRowCache = _hdf5Cache.GetRowCache<byte>(SourceFilename);
			}
			RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
			foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
			{
				if (!ext.Overlaps(rasterFileInfo.Extent))
				{
					continue;
				}
				RasterM rasterM2 = rasterFileInfo.RenderBlockTiles[0];
				RowColRangeM rowColRangeM = rasterM2.RowColRange(ext, limitToRaster: true);
				using TiffFile tiffFile = new TiffFile(rasterFileInfo.Filename);
				int minRow = rowColRangeM.MinRow;
				int maxRow = rowColRangeM.MaxRow;
				for (int k = minRow; k <= maxRow; k++)
				{
					int minCol = rowColRangeM.MinCol;
					int maxCol = rowColRangeM.MaxCol;
					for (int l = minCol; l <= maxCol; l++)
					{
						int cellIndex = rasterM2.CellIndex(k, l);
						if (!rasterFileInfo.TileHasAnyData[0][cellIndex])
						{
							continue;
						}
						rasterM2.SetCellTileRasterM(cellIndex, rasterFileInfo.TileSize[0], rasterM);
						RowColRangeM rowColRangeM2 = spatialIndex.Raster.RowColRange(rasterM, limitToRaster: true);
						bool condition = false;
						int minRow2 = rowColRangeM2.MinRow;
						int maxRow2 = rowColRangeM2.MaxRow;
						for (int m = minRow2; m <= maxRow2; m++)
						{
							int minCol2 = rowColRangeM2.MinCol;
							int maxCol2 = rowColRangeM2.MaxCol;
							for (int n = minCol2; n <= maxCol2; n++)
							{
								List<int> get_Bin = spatialIndex.get_Bin(m, n);
								foreach (int get_Bin2 in get_Bin)
								{
									PointM pointM = points.PointM(get_Bin2);
									if (rasterM.Contains(pointM))
									{
										if (!condition)
										{
											tileVB = tiffFile.Directories[0].GetTileVB<float>(cellIndex);
											ensureGetRow = _hdf5PerimeterRowCache.EnsureGetRow(rasterFileInfo.Groupname + "/" + Strings.Format(0) + "/Perimeter", cellIndex);
											ensureGetRowArray = _hdf5MaskRowCache.EnsureGetRow(rasterFileInfo.Groupname + "/" + Strings.Format(0) + "/Mask", cellIndex);
											renderBlock.SetTile(rasterM, tileVB, ensureGetRow, ensureGetRowArray, stitches);
											condition = true;
										}
										float zPerPoint2 = ComputePointElevationOnTile(rasterM, renderBlock, pointM);
										if (zPerPoint2 != -9999f)
										{
											zPerPoint[get_Bin2] = zPerPoint2;
										}
									}
								}
							}
						}
					}
				}
			}
			Level0StitchTIN()?.ComputePointElevationsIfNoData(points, zPerPoint);
			if (useObstructions)
			{
				_modificationGroup.ObstructPoints(points, zPerPoint, spatialIndex);
			}
		}
	}

	private float ComputePointElevationOnTile(RasterM tile, RasMapperLib.Terrain.RenderBlock renderblock, PointM point)
	{
		int c = default;
		float frac = default;
		tile.ColAndFraction(point.X, ref c, ref frac);
		int r = default;
		float frac2 = default;
		tile.RowAndFraction(point.Y, ref r, ref frac2);
		if (renderblock.SetRowCol(r, c))
		{
			return renderblock.Interpolate(frac, frac2);
		}
		return -9999f;
	}

	public Profile ComputeProfile(SegmentM seg, float filterTol = float.NaN, bool doObstructions = true)
	{
		PointMs pointMs = new PointMs();
		pointMs.Add(seg);
		return ComputeProfile(pointMs, filterTol, doObstructions);
	}

	public Profile ComputeProfile(PointMs polylinePoints, float filterTol = float.NaN, bool doObstructions = true)
	{
		return ComputeProfile(new Polyline(polylinePoints), filterTol, doObstructions);
	}

	public Profile ComputeProfile(Polyline polyline, float filterTol = float.NaN, bool doObstructions = true)
	{
		return ComputeProfiles(new Polyline[1] { polyline }.ToList(), filterTol, null, doObstructions)[0];
	}

	public List<Point2D>[] CutMeshProfiles(MeshFV2D mesh)
	{
		SpatialIndex<int> spatialIndex = mesh.ComputeFaceSpatialIndex();
		RasterM raster = spatialIndex.Raster;
		checked
		{
			List<Point2D>[] point2DListArray = new List<Point2D>[mesh.FaceCount - 1 + 1];
			using (new H5Reader(SourceFilename))
			{
				RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
				foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
				{
					RasterM rasterM = rasterFileInfo.RenderBlockTiles[0];
					if (!rasterM.Overlaps(raster))
					{
						continue;
					}
					new RasterizeSegment(rasterM);
					RowColRangeM rowColRangeM = rasterM.RowColRange(raster, limitToRaster: true);
					using (new FloatTiffReader(rasterFileInfo.Filename))
					{
						int minRow = rowColRangeM.MinRow;
						int maxRow = rowColRangeM.MaxRow;
						for (int j = minRow; j <= maxRow; j++)
						{
							int minCol = rowColRangeM.MinCol;
							int maxCol = rowColRangeM.MaxCol;
							for (int k = minCol; k <= maxCol; k++)
							{
								int cellIndex = rasterM.CellIndex(j, k);
								if (!rasterFileInfo.TileHasAnyData[0][cellIndex])
								{
									continue;
								}
								RasterM cellRasterM = rasterM.GetCellRasterM(cellIndex, rasterFileInfo.TileSize[0]);
								List<int> elements = spatialIndex.GetElements(cellRasterM);
								foreach (int element in elements)
								{
									int ordinal = 0;
									foreach (SegmentM faceSegment in mesh.FaceSegments(element))
									{
										if (faceSegment.MBROverlaps(cellRasterM))
										{
											ordinal++;
										}
									}
								}
							}
						}
					}
				}
			}
			throw new NotImplementedException();
		}
	}

	public Profile[] ComputeProfiles(List<Polyline> polylines, float filterTol = float.NaN, ProgressReporter reporter = null, bool doObstructions = true)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		ProgressReporter progressReporter = reporter.WithProgressModifier(0, 0.6f);
		checked
		{
			Profile[] profiles = new Profile[polylines.Count - 1 + 1];
			int count = polylines.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				profiles[i] = new Profile(DataUnits, DataUnits);
			}
			if (!AllSourceFilesExist())
			{
				return profiles;
			}
			using (H5Reader hr = new H5Reader(SourceFilename))
			{
				int rasterFileCount = RasterFileCount;
				int unity = 1;
				ProgressReporter reporter2 = progressReporter.WithProgressModifier(0, (float)unity / (float)rasterFileCount);
				if (RasterFilesInfo.Count() > 0)
				{
					RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
					foreach (RasterFileInfo rfi in rasterFilesInfo)
					{
						ComputeProfilesOnRaster(hr, rfi, polylines, profiles, filterTol, reporter2);
						unity++;
					}
				}
			}
			progressReporter = reporter.WithProgressModifier(60, 0.2f);
			progressReporter.ReportProgress(0);
			Level0StitchTIN()?.ComputeProfiles(polylines, profiles);
			progressReporter.ReportProgress(100);
			int count2 = profiles.Count() - 1;
			for (int k = 0; k <= count2; k++)
			{
				profiles[k].SortByStation();
			}
			if (doObstructions)
			{
				ObstructProfiles(polylines, profiles, filterTol);
			}
			RasMapperLib.Utilities.Math.ProgressMod(profiles.Count());
			int count3 = profiles.Count() - 1;
			for (int l = 0; l <= count3; l++)
			{
				profiles[l].SortByStation();
			}
			if (!float.IsNaN(filterTol) && filterTol > 0f)
			{
				List<int> indexBuffer = new List<int>();
				int count4 = profiles.Count() - 1;
				for (int m = 0; m <= count4; m++)
				{
					profiles[m] = profiles[m].Filter(filterTol, indexBuffer);
				}
			}
			reporter.ReportProgress(100);
			return profiles;
		}
	}

	public static void OptimizeTerrainForPropertyTables(string terrFn, int rowChunk = 1000, ProgressReporter prog = null)
	{
		if (!File.Exists(terrFn))
		{
			throw new ArgumentException();
		}
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		checked
		{
			using H5Writer h5Writer = new H5Writer(terrFn);
			string path = "/Terrain";
			foreach (string groupName in h5Writer.GetGroupNames(path))
			{
				string pathGroupNameTerm = path + "/" + groupName + "/0";
				string textValue = pathGroupNameTerm + "/Mask";
				string datasetName = pathGroupNameTerm + "/Perimeter";
				string textValue2 = textValue + " Summary";
				int datasetDimension = (int)h5Writer.GetDatasetDimensions(textValue)[0];
				byte[] values = new byte[65536];
				byte[] byteValues = new byte[datasetDimension - 1 + 1];
				prog.ReportMessage("Creating Summary Dataset...");
				int count = datasetDimension - 1;
				for (int i = 0; i <= count; i++)
				{
					h5Writer.ReadRowVB(textValue, i, ref values);
					bool condition = true;
					bool condition2 = true;
					int count2 = values.Count() - 1;
					for (int j = 0; j <= count2; j++)
					{
						byte b = values[j];
						if (b == 0 && condition)
						{
							condition2 = false;
							continue;
						}
						condition = false;
						if (b != 1 || !condition2)
						{
							condition2 = false;
							break;
						}
					}
					if (condition)
					{
						byteValues[i] = 0;
					}
					else if (condition2)
					{
						byteValues[i] = 1;
					}
					else
					{
						byteValues[i] = byte.MaxValue;
					}
					prog.ReportProgress((int)System.Math.Round(100.0 * (double)(i + 1) / (double)datasetDimension));
				}
				prog.ReportProgress(100);
				if (h5Writer.ExistsAsDataset(textValue2))
				{
					h5Writer.Delete(textValue2);
				}
				h5Writer.WriteArray(textValue2, byteValues);
				prog.ReportProgress(0);
				prog.ReportMessage("Recompressing datasets for optimal reading");
				h5Writer.Rechunk2D<byte>(textValue, rowChunk);
				h5Writer.Rechunk2D<float>(datasetName, rowChunk);
			}
		}
	}

	private void ComputeProfilesOnRaster(H5Reader hr, RasterFileInfo rfi, List<Polyline> polylines, Profile[] profileData, float filterTol, ProgressReporter reporter = null)
	{
		_Closure_0024__134_002D0 capturedLocals = new _Closure_0024__134_002D0(null);
		capturedLocals.terrainLayer = this;
		capturedLocals.rfi = rfi;
		capturedLocals.polylines = polylines;
		capturedLocals.filterTol = filterTol;
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		capturedLocals.tiles = capturedLocals.rfi.RenderBlockTiles[0];
		capturedLocals.tilesToProcess = new Dictionary<int, List<PolylineSegmentStartStation>>();
		RasterizeSegment rasterizeSegment = new RasterizeSegment(capturedLocals.tiles);
		checked
		{
			int count2 = capturedLocals.polylines.Count - 1;
			_Closure_0024__134_002D1 capturedLocals2 = default;
			_Closure_0024__134_002D2 capturedLocals3 = default;
			for (int i = 0; i <= count2; i++)
			{
				capturedLocals2 = new _Closure_0024__134_002D1(capturedLocals2);
				capturedLocals2.outerClosure = capturedLocals;
				capturedLocals2.pIndex = i;
				Polyline polyline = capturedLocals2.outerClosure.polylines[i];
				capturedLocals2.segStartStation = 0.0;
				int count3 = polyline.SegmentCount() - 1;
				for (int j = 0; j <= count3; j++)
				{
					capturedLocals3 = new _Closure_0024__134_002D2(capturedLocals3);
					capturedLocals3.outerClosure = capturedLocals2;
					capturedLocals3.segIndex = j;
					SegmentM segment = polyline.Segment(j);
					if (segment.ClipToExtent(capturedLocals3.outerClosure.outerClosure.tiles))
					{
						rasterizeSegment.ComputeCells(segment, capturedLocals3._Lambda_0024__0);
					}
					capturedLocals3.outerClosure.segStartStation = capturedLocals3.outerClosure.segStartStation + polyline.Segment(j).Length;
				}
			}
			if (capturedLocals.tilesToProcess.Count == 0)
			{
				return;
			}
			capturedLocals.tileIdxMap = new Dictionary<int, int>();
			capturedLocals.maskRowSplitter = null;
			capturedLocals.perimRowSplitter = null;
			List<int> intList = capturedLocals.tilesToProcess.Select([SpecialName] (KeyValuePair<int, List<PolylineSegmentStartStation>> kvp) => kvp.Key).ToList();
			intList.Sort();
			int systemTerm = (int)System.Math.Ceiling((float)intList.Count / 1000f);
			capturedLocals.maskRowSplitter = new byte[systemTerm - 1 + 1][];
			capturedLocals.perimRowSplitter = new float[systemTerm - 1 + 1][];
			if (_hdf5PerimeterRowCache == null)
			{
				_hdf5PerimeterRowCache = _hdf5Cache.GetRowCache<float>(SourceFilename);
			}
			if (_hdf5MaskRowCache == null)
			{
				_hdf5MaskRowCache = _hdf5Cache.GetRowCache<byte>(SourceFilename);
			}
			int count4 = systemTerm - 1;
			for (int intervalIndex = 0; intervalIndex <= count4; intervalIndex++)
			{
				int ordinal = 1000 * intervalIndex;
				int count = System.Math.Min(intList.Count - ordinal, 1000);
				List<int> range = intList.GetRange(ordinal, count);
				List<byte> byteList = new List<byte>();
				List<float> floatList = new List<float>();
				int count5 = range.Count - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count5; intervalIndex2++)
				{
					byte[] collection = _hdf5MaskRowCache.EnsureGetRow(capturedLocals.rfi.Groupname + "/0/Mask", range[intervalIndex2]);
					byteList.AddRange(collection);
					float[] collection2 = _hdf5PerimeterRowCache.EnsureGetRow(capturedLocals.rfi.Groupname + "/0/Perimeter", range[intervalIndex2]);
					floatList.AddRange(collection2);
				}
				capturedLocals.maskRowSplitter[intervalIndex] = byteList.ToArray();
				capturedLocals.perimRowSplitter[intervalIndex] = floatList.ToArray();
			}
			int count6 = intList.Count - 1;
			for (int intervalIndex3 = 0; intervalIndex3 <= count6; intervalIndex3++)
			{
				capturedLocals.tileIdxMap[intList[intervalIndex3]] = intervalIndex3;
			}
			_Closure_0024__134_002D4 capturedLocals4 = new _Closure_0024__134_002D4(null);
			capturedLocals4.outerClosure = capturedLocals;
			capturedLocals4.tr = new TiffFile(capturedLocals4.outerClosure.rfi.Filename);
			try
			{
				_Closure_0024__134_002D3 capturedLocals5 = default;
				_Closure_0024__134_002D3 capturedLocals6 = new _Closure_0024__134_002D3(capturedLocals5);
				capturedLocals6.outerClosure = capturedLocals4;
				ForceLoadObstructions();
				RuntimeHelpers.GetObjectValue(new object());
				capturedLocals6.threadLocalProfileData = new ThreadLocal<Dictionary<int, Profile>>([SpecialName] () => new Dictionary<int, Profile>(), trackAllValues: true);
				capturedLocals6.tlMask = new ThreadLocal<byte[]>([SpecialName] () => new byte[65536], trackAllValues: true);
				capturedLocals6.tlPerim = new ThreadLocal<float[]>([SpecialName] () => new float[513], trackAllValues: true);
				capturedLocals6.tlTile = new ThreadLocal<float[]>([SpecialName] () => new float[65536], trackAllValues: true);
				System.Threading.Tasks.Parallel.ForEach(capturedLocals6.outerClosure.outerClosure.tilesToProcess, [SpecialName] (KeyValuePair<int, List<PolylineSegmentStartStation>> t2p) =>
				{
					int key = t2p.Key;
					if (capturedLocals6.outerClosure.outerClosure.rfi.TileHasAnyData[0][key])
					{
						Dictionary<int, Profile> profileByInt = capturedLocals6.threadLocalProfileData.Value;
						List<PolylineSegmentStartStation> polylineSegmentStartStations = t2p.Value;
						RasterM rasterM = new RasterM();
						capturedLocals6.outerClosure.outerClosure.tiles.SetCellTileRasterM(key, capturedLocals6.outerClosure.outerClosure.rfi.TileSize[0], rasterM);
						RasterizeSegment tileRS = new RasterizeSegment(rasterM);
						float[] floatValues = capturedLocals6.tlPerim.Value;
						float[] getTileVBDirectoriesTerm = capturedLocals6.tlTile.Value;
						byte[] byteValues = capturedLocals6.tlMask.Value;
						getTileVBDirectoriesTerm = capturedLocals6.outerClosure.tr.Directories[0].GetTileVB<float>(key);
						int ordinal2 = capturedLocals6.outerClosure.outerClosure.tileIdxMap[key];
						byte[] src = capturedLocals6.outerClosure.outerClosure.maskRowSplitter[(int)System.Math.Floor((double)ordinal2 / 1000.0)];
						float[] src2 = capturedLocals6.outerClosure.outerClosure.perimRowSplitter[(int)System.Math.Floor((double)ordinal2 / 1000.0)];
						ordinal2 = unchecked(ordinal2 % 1000);
						Buffer.BlockCopy(src, ordinal2 * byteValues.Length, byteValues, 0, byteValues.Length);
						Buffer.BlockCopy(src2, ordinal2 * floatValues.Length * 4, floatValues, 0, floatValues.Length * 4);
						RasMapperLib.Terrain.RenderBlock renderBlock = new RasMapperLib.Terrain.RenderBlock();
						renderBlock.SetTile(rasterM, getTileVBDirectoriesTerm, floatValues, byteValues, null);
						int count7 = polylineSegmentStartStations.Count - 1;
						for (int k = 0; k <= count7; k++)
						{
							PolylineSegmentStartStation polylineSegmentStartStation = polylineSegmentStartStations[k];
							int polylineIndex = polylineSegmentStartStation.PolylineIndex;
							int segmentIndex = polylineSegmentStartStation.SegmentIndex;
							Profile profile = null;
							if (!profileByInt.TryGetValue(polylineIndex, out profile))
							{
								profile = (profileByInt[polylineIndex] = new Profile());
							}
							SegmentM segment2 = capturedLocals6.outerClosure.outerClosure.polylines[polylineIndex].Segment(segmentIndex);
							double segmentStartingStation = polylineSegmentStartStation.SegmentStartingStation;
							Profile profile2 = capturedLocals6.outerClosure.outerClosure.terrainLayer.ComputeProfilesOnTile(rasterM, tileRS, renderBlock, segment2, segmentStartingStation);
							if (float.IsNaN(capturedLocals6.outerClosure.outerClosure.filterTol) | (capturedLocals6.outerClosure.outerClosure.filterTol <= 0f))
							{
								profile.AddRange(profile2);
							}
							else
							{
								List<int> filterKeepList = profile2.FilterKeepList(capturedLocals6.outerClosure.outerClosure.filterTol);
								foreach (int filterKeepList2 in filterKeepList)
								{
									profile.Add(profile2[filterKeepList2]);
								}
							}
						}
					}
				});
				foreach (Dictionary<int, Profile> localValue in capturedLocals6.threadLocalProfileData.Values)
				{
					foreach (KeyValuePair<int, Profile> localValue2 in localValue)
					{
						profileData[localValue2.Key].AddRange(localValue2.Value);
					}
				}
			}
			finally
			{
				if (capturedLocals4.tr != null)
				{
					((IDisposable)capturedLocals4.tr).Dispose();
				}
			}
		}
	}

	private void ComputeProfilesOnTile(RasterM tile, RasterizeSegment tileRS, RasMapperLib.Terrain.RenderBlock renderblock, SegmentM segment, double startingStation, Profile profile)
	{
		_Closure_0024__135_002D0 capturedLocals = default;
		_Closure_0024__135_002D0 capturedLocals2 = new _Closure_0024__135_002D0(capturedLocals);
		capturedLocals2.renderblock = renderblock;
		capturedLocals2.segment = segment;
		capturedLocals2.startingStation = startingStation;
		capturedLocals2.profile = profile;
		capturedLocals2.profile.Clear();
		capturedLocals2.segLength = capturedLocals2.segment.Length;
		if (tile.Contains(capturedLocals2.segment.A))
		{
			float pointElevationOnTile = ComputePointElevationOnTile(tile, capturedLocals2.renderblock, capturedLocals2.segment.A);
			if (pointElevationOnTile != -9999f)
			{
				capturedLocals2.profile.Add(capturedLocals2.startingStation, pointElevationOnTile);
			}
		}
		if (tile.Contains(capturedLocals2.segment.B))
		{
			float pointElevationOnTile2 = ComputePointElevationOnTile(tile, capturedLocals2.renderblock, capturedLocals2.segment.B);
			if (pointElevationOnTile2 != -9999f)
			{
				capturedLocals2.profile.Add(capturedLocals2.startingStation + capturedLocals2.segLength, pointElevationOnTile2);
			}
		}
		Action<int, int> fillCellAction = [SpecialName] (int rbRow, int rbCol) =>
		{
			if (capturedLocals2.renderblock.SetRowCol(rbRow, rbCol))
			{
				PointM pointM = capturedLocals2.renderblock.PointA(rbRow, rbCol);
				PointM pointM2 = capturedLocals2.renderblock.PointB(rbRow, rbCol);
				PointM pointM3 = capturedLocals2.renderblock.PointC(rbRow, rbCol);
				PointM pointM4 = capturedLocals2.renderblock.PointD(rbRow, rbCol);
				PointM pA = capturedLocals2.renderblock.PointE(rbRow, rbCol);
				int ordinal = 0;
				double x = default;
				double y = default;
				double t = default;
				double t2 = default;
				do
				{
					switch (ordinal)
					{
					case 0:
						capturedLocals2.triEdge.Assign(pA, pointM);
						break;
					case 1:
						capturedLocals2.triEdge.B = pointM2;
						break;
					case 2:
						capturedLocals2.triEdge.B = pointM3;
						break;
					case 3:
						capturedLocals2.triEdge.B = pointM4;
						break;
					case 4:
						capturedLocals2.triEdge.Assign(pointM, pointM2);
						break;
					case 5:
						capturedLocals2.triEdge.Assign(pointM2, pointM3);
						break;
					case 6:
						capturedLocals2.triEdge.Assign(pointM3, pointM4);
						break;
					case 7:
						capturedLocals2.triEdge.Assign(pointM4, pointM);
						break;
					}
					if (capturedLocals2.segment.Intersects(capturedLocals2.triEdge, ref x, ref y, ref t, ref t2))
					{
						float y2 = (float)capturedLocals2.triEdge.ZAtFraction(t2);
						capturedLocals2.profile.Add((float)(capturedLocals2.startingStation + capturedLocals2.segLength * t), y2);
					}
					ordinal = checked(ordinal + 1);
				}
				while (ordinal <= 7);
			}
		};
		tileRS.ComputeCells(capturedLocals2.segment, fillCellAction);
		capturedLocals2.profile.SortByStation();
	}

	private Profile ComputeProfilesOnTile(RasterM tile, RasterizeSegment tileRS, RasMapperLib.Terrain.RenderBlock renderblock, SegmentM segment, double startingStation)
	{
		_Closure_0024__136_002D0 capturedLocals = default;
		_Closure_0024__136_002D0 capturedLocals2 = new _Closure_0024__136_002D0(capturedLocals);
		capturedLocals2.renderblock = renderblock;
		capturedLocals2.segment = segment;
		capturedLocals2.startingStation = startingStation;
		capturedLocals2.profile = new Profile();
		capturedLocals2.segLength = capturedLocals2.segment.Length;
		Extent extent = tile.ComputeExtent();
		if (extent.RoughlyContains(capturedLocals2.segment.A, 1E-07))
		{
			float pointElevationOnTile = ComputePointElevationOnTile(tile, capturedLocals2.renderblock, capturedLocals2.segment.A);
			if (pointElevationOnTile != -9999f)
			{
				capturedLocals2.profile.Add(capturedLocals2.startingStation, pointElevationOnTile);
			}
		}
		if (extent.RoughlyContains(capturedLocals2.segment.B, 1E-07))
		{
			float pointElevationOnTile2 = ComputePointElevationOnTile(tile, capturedLocals2.renderblock, capturedLocals2.segment.B);
			if (pointElevationOnTile2 != -9999f)
			{
				capturedLocals2.profile.Add(capturedLocals2.startingStation + capturedLocals2.segLength, pointElevationOnTile2);
			}
		}
		Action<int, int> fillCellAction = [SpecialName] (int rbRow, int rbCol) =>
		{
			if (capturedLocals2.renderblock.SetRowCol(rbRow, rbCol))
			{
				PointM pointM = capturedLocals2.renderblock.PointA(rbRow, rbCol);
				PointM pointM2 = capturedLocals2.renderblock.PointB(rbRow, rbCol);
				PointM pointM3 = capturedLocals2.renderblock.PointC(rbRow, rbCol);
				PointM pointM4 = capturedLocals2.renderblock.PointD(rbRow, rbCol);
				PointM pA = capturedLocals2.renderblock.PointE(rbRow, rbCol);
				int ordinal = 0;
				double x = default;
				double y = default;
				double t = default;
				double t2 = default;
				do
				{
					switch (ordinal)
					{
					case 0:
						capturedLocals2.triEdge.Assign(pA, pointM);
						break;
					case 1:
						capturedLocals2.triEdge.B = pointM2;
						break;
					case 2:
						capturedLocals2.triEdge.B = pointM3;
						break;
					case 3:
						capturedLocals2.triEdge.B = pointM4;
						break;
					case 4:
						capturedLocals2.triEdge.Assign(pointM, pointM2);
						break;
					case 5:
						capturedLocals2.triEdge.Assign(pointM2, pointM3);
						break;
					case 6:
						capturedLocals2.triEdge.Assign(pointM3, pointM4);
						break;
					case 7:
						capturedLocals2.triEdge.Assign(pointM4, pointM);
						break;
					}
					if (capturedLocals2.segment.Intersects(capturedLocals2.triEdge, ref x, ref y, ref t, ref t2))
					{
						float y2 = (float)capturedLocals2.triEdge.ZAtFraction(t2);
						capturedLocals2.profile.Add((float)(capturedLocals2.startingStation + capturedLocals2.segLength * t), y2);
					}
					ordinal = checked(ordinal + 1);
				}
				while (ordinal <= 7);
			}
		};
		tileRS.ComputeCells(capturedLocals2.segment, fillCellAction, 1E-07);
		capturedLocals2.profile.SortByStation();
		return capturedLocals2.profile;
	}

	public Point2DCollection ComputeElevationVolumeTable(Polygon polygon, double VolumeElevationFilterTol = 0.01, double volumeFactor = 1.0, int verticalHistogramCount = 65536)
	{
		if (!Polygon.IsValidPolygon(polygon))
		{
			return null;
		}
		double area = polygon.Area;
		double area2 = ((SharedData.Units != SharedData.EnumUnits.SIUnits) ? area : (area / 0.3048 / 0.3048));
		int ordinal = 1000000;
		if (area2 > 100000000.0)
		{
			ordinal = 5000000;
		}
		if (area2 > 500000000.0)
		{
			ordinal = 10000000;
		}
		checked
		{
			bool[] boolValues = new bool[ordinal - 1 + 1];
			float[] values = new float[ordinal - 1 + 1];
			double absoluteArea = System.Math.Abs(polygon.Area);
			RasterM rasterM = RasterM.ComputeRasterM(ordinal, polygon.MaxX, polygon.MinX, polygon.MaxY, polygon.MinY);
			Resample(rasterM, ref values);
			polygon.Rasterize(rasterM, boolValues);
			float minValue = float.MinValue;
			float maxValue = float.MaxValue;
			long ordinal2 = 0L;
			long ordinal3 = 0L;
			int count = rasterM.Cols * rasterM.Rows - 1;
			for (int i = 0; i <= count; i++)
			{
				if (!boolValues[i])
				{
					continue;
				}
				ordinal2++;
				float computed = values[i];
				if (computed != -9999f)
				{
					ordinal3++;
					if (computed > minValue)
					{
						minValue = computed;
					}
					if (computed < maxValue)
					{
						maxValue = computed;
					}
				}
			}
			if (ordinal3 == 0L)
			{
				return new Point2DCollection();
			}
			double area3 = area;
			if (ordinal2 != ordinal3)
			{
				area3 = (double)ordinal3 * rasterM.CellArea;
			}
			Point2DCollection point2DCollection = new Point2DCollection();
			point2DCollection.Add(0f, maxValue);
			if (minValue > maxValue)
			{
				Histogram histogram = new Histogram(maxValue, minValue, verticalHistogramCount);
				double cellArea = rasterM.CellArea;
				float minValue2 = float.MinValue;
				int unity = 0;
				int count2 = rasterM.Cols * rasterM.Rows - 1;
				for (int j = 0; j <= count2; j++)
				{
					float minValue22 = values[j];
					if (boolValues[j] && minValue22 != -9999f)
					{
						if (minValue22 == minValue2)
						{
							unity++;
							continue;
						}
						histogram.AddArea(minValue2, cellArea * (double)unity);
						minValue2 = minValue22;
						unity = 1;
					}
				}
				histogram.AddArea(minValue2, cellArea * (double)unity);
				Point2DCollection point2DCollection2 = new Point2DCollection();
				histogram.ComputeCumulativeValues(point2DCollection2);
				float ratio = (float)(absoluteArea / (double)point2DCollection2.Last().X);
				point2DCollection2.RatioX(ratio);
				float computed2 = 0f;
				int count3 = point2DCollection2.Count - 1;
				for (int k = 1; k <= count3; k++)
				{
					float point2DCollection2Term = (float)(0.5 * (double)(point2DCollection2[k - 1].X + point2DCollection2[k].X));
					float point2DCollection2Term2 = point2DCollection2[k].Y - point2DCollection2[k - 1].Y;
					computed2 += point2DCollection2Term2 * point2DCollection2Term;
					point2DCollection.Add((double)computed2 / volumeFactor, point2DCollection2[k].Y);
				}
				point2DCollection = point2DCollection.Filter((float)VolumeElevationFilterTol);
			}
			Point2D point2D = point2DCollection.Last();
			float x = point2D.X;
			float y = point2D.Y;
			point2DCollection.Add((double)x + area3 / volumeFactor, y + 1f);
			return point2DCollection;
		}
	}

	internal List<SegmentM> Level0Stitches()
	{
		if (_level0Stitches == null)
		{
			try
			{
				_level0Stitches = ReadLevel0Stitches(SourceFilename);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ErrorMessage = ex2.Message;
				DrawNode();
				ProjectData.ClearProjectError();
			}
		}
		return _level0Stitches;
	}

	public static List<SegmentM> ReadLevel0Stitches(string terrainFilename)
	{
		List<SegmentM> segmentMs = new List<SegmentM>();
		checked
		{
			using (H5Reader h5Reader = new H5Reader(terrainFilename))
			{
				if (!h5Reader.ExistsAsDataset(Geospatial.Terrain.Terrain.GetStitchesName()))
				{
					return segmentMs;
				}
				double[,] values = new double[1, 1];
				string stitchesName = Geospatial.Terrain.Terrain.GetStitchesName();
				long[] datasetDimensions = h5Reader.GetDatasetDimensions(stitchesName);
				int systemDatasetDimensionsTerm = (int)System.Math.Floor((double)datasetDimensions[0] / 25000.0);
				int count = systemDatasetDimensionsTerm - 1;
				for (int i = 0; i <= count; i++)
				{
					int startRow = i * 25000;
					h5Reader.ReadRows(stitchesName, startRow, 25000, ref values);
					int count2 = values.GetLength(0) - 1;
					for (int j = 0; j <= count2; j++)
					{
						double px = values[j, 0];
						double py = values[j, 1];
						double pz = values[j, 2];
						double px2 = values[j, 3];
						double py2 = values[j, 4];
						double pz2 = values[j, 5];
						double pm = values[j, 6];
						SegmentM segmentM = new SegmentM(new PointM(px, py, pz, pm), new PointM(px2, py2, pz2, pm));
						segmentMs.Add(segmentM);
					}
				}
				int ordinal = systemDatasetDimensionsTerm * 25000;
				int datasetDimensionsTerm = (int)(datasetDimensions[0] - ordinal);
				if (datasetDimensionsTerm != 0)
				{
					h5Reader.ReadRows(stitchesName, systemDatasetDimensionsTerm * 25000, datasetDimensionsTerm, ref values);
					int count3 = values.GetLength(0) - 1;
					for (int k = 0; k <= count3; k++)
					{
						double px3 = values[k, 0];
						double py3 = values[k, 1];
						double pz3 = values[k, 2];
						double px4 = values[k, 3];
						double py4 = values[k, 4];
						double pz4 = values[k, 5];
						double pm2 = values[k, 6];
						SegmentM segmentM2 = new SegmentM(new PointM(px3, py3, pz3, pm2), new PointM(px4, py4, pz4, pm2));
						segmentMs.Add(segmentM2);
					}
				}
			}
			return segmentMs;
		}
	}

	public Tin Level0StitchTIN()
	{
		checked
		{
			if (_level0StitchTIN == null && !_triedStitchReading)
			{
				try
				{
					_triedStitchReading = true;
					_level0StitchTIN = ReadLevel0StitchTIN(SourceFilename);
					if (_level0StitchTIN == null)
					{
						return null;
					}
					Extent ext = ((_level0StitchTIN.TriangleCount() <= 0) ? new Extent(1.0, 0.0, 1.0, 0.0) : _level0StitchTIN.ComputeExtent());
					_stitchTINSpatialIndex = new DistinctSpatialIndex(ext, _level0StitchTIN.TriangleCount());
					int count = _level0StitchTIN.TriangleCount() - 1;
					for (int i = 0; i <= count; i++)
					{
						_stitchTINSpatialIndex.Add(_level0StitchTIN.TriangleExtent(i), i);
					}
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					ErrorMessage = ex2.Message;
					ProjectData.ClearProjectError();
				}
			}
			return _level0StitchTIN;
		}
	}

	public static Tin ReadLevel0StitchTIN(string terrainFilename)
	{
		checked
		{
			using H5Reader h5Reader = new H5Reader(terrainFilename);
			if (h5Reader.ExistsAsDataset(Geospatial.Terrain.Terrain.GetStitchTinName() + " Points"))
			{
				PointMs pointMs = new PointMs();
				using (HDFBufferedReader<double> hDFBufferedReader = new HDFBufferedReader<double>(h5Reader, Geospatial.Terrain.Terrain.GetStitchTinName() + " Points"))
				{
					int count = hDFBufferedReader.Rows - 1;
					PointM pointM = default;
					for (int i = 0; i <= count; i++)
					{
						hDFBufferedReader.ReadRow(ref pointM.X, ref pointM.Y, ref pointM.Z, ref pointM.M);
						pointMs.Add(pointM);
					}
				}
				TriangleStruct[] triangleStructs;
				using (HDFBufferedReader<int> hDFBufferedReader2 = new HDFBufferedReader<int>(h5Reader, Geospatial.Terrain.Terrain.GetStitchTinName() + " Triangles"))
				{
					triangleStructs = new TriangleStruct[hDFBufferedReader2.Rows - 1 + 1];
					int count2 = hDFBufferedReader2.Rows - 1;
					for (int j = 0; j <= count2; j++)
					{
						hDFBufferedReader2.ReadRow(ref triangleStructs[j].p0, ref triangleStructs[j].p1, ref triangleStructs[j].p2);
					}
				}
				return new Tin(pointMs, triangleStructs);
			}
			return null;
		}
	}

	private void ClipTerrainToExtent(Extent extent, ProgressReporter prog, Func<bool> checkIfCancelled, Action<SpatialIndex<int>> addItemsToSpIdx, List<string> newRFIs, ref TiffMetadata<float> md)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		int ordinal = 0;
		int rasterFilesInfoTerm = RasterFilesInfo.Count();
		if (!checkIfCancelled())
		{
			return;
		}
		RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
		checked
		{
			foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
			{
				double cellSize = rasterFileInfo.CellSize[0];
				string combinePathTerm = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(rasterFileInfo.Filename), System.IO.Path.GetFileNameWithoutExtension(rasterFileInfo.Filename)) + "_clip";
				combinePathTerm = combinePathTerm.Replace(Name + ".", "");
				combinePathTerm = FileHelpers.GetUniqueFileName(combinePathTerm, ".tif");
				if (extent.IntersectionXY(rasterFileInfo.Extent) == null)
				{
					continue;
				}
				HashSet<int> intSet = new HashSet<int>();
				HashSet<int> intSetSet = new HashSet<int>();
				RasterM rasterM = rasterFileInfo.TileRasters[0];
				SpatialIndex<int> spatialIndex = new SpatialIndex<int>(rasterM);
				addItemsToSpIdx(spatialIndex);
				int maxValue = int.MaxValue;
				int minValue = int.MinValue;
				int maxValue2 = int.MaxValue;
				int minValue2 = int.MinValue;
				double maxValue3 = double.MaxValue;
				double minValue3 = double.MinValue;
				int count = rasterM.Rows - 1;
				for (int j = 0; j <= count; j++)
				{
					int count2 = rasterM.Cols - 1;
					for (int k = 0; k <= count2; k++)
					{
						int cellIndex = rasterM.CellIndex(j, k);
						Extent extent2 = rasterM.CellExtent(cellIndex);
						if (spatialIndex.GetElements(extent2.CenterPoint()).Count > 0)
						{
							if (k < maxValue)
							{
								maxValue = k;
							}
							if (k > minValue)
							{
								minValue = k;
							}
							if (j < maxValue2)
							{
								maxValue2 = j;
							}
							if (j > minValue2)
							{
								minValue2 = j;
							}
							if (extent2.MinX < maxValue3)
							{
								maxValue3 = extent2.MinX;
							}
							if (extent2.MaxY > minValue3)
							{
								minValue3 = extent2.MaxY;
							}
							intSet.Add(cellIndex);
						}
					}
				}
				int maxValue4 = maxValue2;
				int minValue4 = minValue2;
				for (int l = maxValue4; l <= minValue4; l++)
				{
					int maxValue5 = maxValue;
					int minValue5 = minValue;
					for (int m = maxValue5; m <= minValue5; m++)
					{
						int cellIndex2 = rasterM.CellIndex(l, m);
						if (!intSet.Contains(cellIndex2))
						{
							intSetSet.Add(cellIndex2);
							intSet.Add(cellIndex2);
						}
					}
				}
				intSet.OrderBy([SpecialName] (int idx) => idx).ToList();
				int ordinal2 = 256;
				int minValue2MaxValue2Term = (minValue2 - maxValue2 + 1) * ordinal2;
				int minValueMaxValueTerm = (minValue - maxValue + 1) * ordinal2;
				double maxValue3CellSizeTerm = maxValue3 + (double)minValueMaxValueTerm * cellSize;
				double minValue3CellSizeTerm = minValue3 - (double)minValue2MaxValue2Term * cellSize;
				if (maxValue3CellSizeTerm > rasterFileInfo.Extent.MaxX)
				{
					double maxXExtentTerm = maxValue3CellSizeTerm - rasterFileInfo.Extent.MaxX;
					minValueMaxValueTerm -= (int)System.Math.Round(maxXExtentTerm / cellSize);
				}
				if (minValue3CellSizeTerm < rasterFileInfo.Extent.MinY)
				{
					double minYExtentTerm = rasterFileInfo.Extent.MinY - minValue3CellSizeTerm;
					minValue2MaxValue2Term -= (int)System.Math.Round(minYExtentTerm / cellSize);
				}
				if (md == null)
				{
					md = new TiffMetadata<float>(-9999f, RoundTo.None);
					md.DataType = DataType;
					md.DataUnits = DataUnits;
					if (SharedData.Units == SharedData.EnumUnits.SIUnits)
					{
						md.Rounding = RoundTo.HundredTwentyEighths;
					}
					else
					{
						md.Rounding = RoundTo.ThirtySeconds;
					}
					md.TileHeight = ordinal2;
					md.TileWidth = ordinal2;
				}
				prog.ReportMessage("Exporting Terrain " + (ordinal + 1) + "/" + rasterFilesInfoTerm);
				new Dictionary<string, TiffFile>().Add(rasterFileInfo.Filename, new TiffFile(rasterFileInfo.Filename));
				newRFIs.Add(combinePathTerm);
				throw new NotImplementedException("Need to migrate tiffassist method here");
			}
		}
	}

	private void CopyTerrainModificationsToTerrainLayer(TerrainLayer other)
	{
		if (other.Modifications.AllModifiers().Count() == 0 && !other.Nodes.Contains(other.Modifications))
		{
			other.Nodes.Insert(0, other.Modifications);
		}
		other.Modifications.CopyModificationsFrom(new IElevationModificationLayer[1] { Modifications });
		other.Modifications.WriteModificationsToHDF();
	}

	public void ExportResampleToSingleFile(string newFilename, double cellSize, TiffMetadata<float> md = null, ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		Extent extent = Extent;
		checked
		{
			int systemHeightTerm = (int)System.Math.Ceiling(extent.Height / cellSize);
			int systemWidthTerm = (int)System.Math.Ceiling(extent.Width / cellSize);
			int ordinal = 256;
			double minX = Extent.MinX;
			double maxY = Extent.MaxY;
			_ = (double)systemWidthTerm;
			_ = (double)systemHeightTerm;
			if (md == null)
			{
				md = new TiffMetadata<float>(-9999f, RoundTo.None);
				md.DataType = DataType;
				md.DataUnits = DataUnits;
				if (SharedData.Units == SharedData.EnumUnits.SIUnits)
				{
					md.Rounding = RoundTo.HundredTwentyEighths;
				}
				else
				{
					md.Rounding = RoundTo.ThirtySeconds;
				}
				md.TileHeight = ordinal;
				md.TileWidth = ordinal;
			}
			prog.ReportMessage("Exporting Terrain");
			using (FloatTiffWriter floatTiffWriter = new FloatTiffWriter(newFilename, systemWidthTerm, systemHeightTerm, ComputeStats: true, md))
			{
				int numTilesWide = floatTiffWriter.GetNumTilesWide();
				int numTilesTall = floatTiffWriter.GetNumTilesTall();
				int numTilesWideTimesNumTilesTall = numTilesWide * numTilesTall;
				double maxX = minX + (double)numTilesWide * cellSize * (double)ordinal;
				double minY = maxY - (double)numTilesTall * cellSize * (double)ordinal;
				RasterM rasterM = new RasterM(numTilesTall, numTilesWide, maxX, minX, maxY, minY);
				RasterM raster = new RasterM();
				float[] values = null;
				int count = numTilesTall - 1;
				for (int i = 0; i <= count; i++)
				{
					int count2 = numTilesWide - 1;
					for (int j = 0; j <= count2; j++)
					{
						int cellIndex = rasterM.CellIndex(i, j);
						rasterM.SetCellTileRasterM(cellIndex, ordinal, raster);
						Resample(raster, ref values);
						floatTiffWriter.WriteTile(cellIndex, values);
						prog.ReportProgress((int)System.Math.Round((double)(100 * (cellIndex + 1)) / (double)numTilesWideTimesNumTilesTall));
					}
				}
			}
			prog.ReportMessage("Exporting Terrain (creating histogram)");
			InterpolatedLayer.AddHistogram(newFilename);
			prog.ReportMessage("Exporting Terrain (adding overlays)");
			InterpolatedLayer.AddSRSGeorefAddo(newFilename, systemWidthTerm, systemHeightTerm, minX, maxY, cellSize, UseNearest: false, null, null, null);
			prog.ReportMessage("Terrain Export (complete)");
		}
	}

	public void GenerateContours(object sender, EventArgs e)
	{
		int ProcessTimeMS = 0;
		if (double.TryParse(Interaction.InputBox("Enter Top value"), out var result) && double.TryParse(Interaction.InputBox("Enter Bottom value"), out var result2))
		{
			PolygonFeatureLayer polygonFeatureLayer = new PolygonFeatureLayer("Contour Band");
			ContourBand(polygonFeatureLayer, result, result2, ref ProcessTimeMS);
			SharedData.RasMapper.StatusMessageAdd("Contour band generation time: " + Form.MillisecondsToString(ProcessTimeMS), replacePRev: false);
			SharedData.RasMapper.MapLayersGroup.Nodes.Add(polygonFeatureLayer);
			polygonFeatureLayer.Checked = true;
		}
	}

	public void ContourBand(PolygonFeatureLayer layer, double rangeHi, double rangelo, ref int ProcessTimeMS)
	{
		int tickCount = Environment.TickCount;
		try
		{
			ProcessTimeMS = checked(Environment.TickCount - tickCount);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
		throw new NotImplementedException();
	}

	public void Rename(string newTerrainName, Dictionary<string, string> tiffFilenameMap, bool updateLayerName)
	{
		string directoryName = System.IO.Path.GetDirectoryName(SourceFilename);
		foreach (KeyValuePair<string, string> tiffFilenameMap2 in tiffFilenameMap)
		{
			if (!File.Exists(System.IO.Path.Combine(directoryName, tiffFilenameMap2.Key)))
			{
				throw new FileNotFoundException(tiffFilenameMap2.Key + " wasn't found in " + directoryName);
			}
			if (File.Exists(System.IO.Path.Combine(directoryName, tiffFilenameMap2.Value)))
			{
				throw new ArgumentException(tiffFilenameMap2.Value + " already exists in " + directoryName);
			}
		}
		if (updateLayerName)
		{
			Name = System.IO.Path.GetFileNameWithoutExtension(newTerrainName);
		}
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if (!h5Reader.ExistsAsGroup("Terrain"))
			{
				throw new Exception("Malformed terrain file.");
			}
			List<string> groupNames = h5Reader.GetGroupNames("Terrain");
			foreach (KeyValuePair<string, string> tiffFilenameMap3 in tiffFilenameMap)
			{
				if (!groupNames.Contains(System.IO.Path.GetFileNameWithoutExtension(tiffFilenameMap3.Key)))
				{
					throw new ArgumentException("Requested filenames don't match terrain file references.");
				}
			}
		}
		using (H5Writer h5Writer = new H5Writer(SourceFilename))
		{
			List<string> groupNames2 = h5Writer.GetGroupNames("Terrain");
			foreach (string groupNames22 in groupNames2)
			{
				string textValue = "";
				if (tiffFilenameMap.TryGetValue(groupNames22 + ".tif", out textValue))
				{
					string groupNames22Term = "Terrain/" + groupNames22;
					string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(textValue);
					string newGroupName = "Terrain/" + fileNameWithoutExtension;
					h5Writer.WriteAttribute(groupNames22Term, "File", textValue);
					h5Writer.RenameGroup(groupNames22Term, newGroupName);
				}
			}
		}
		if (!newTerrainName.EndsWith(".hdf"))
		{
			newTerrainName += ".hdf";
		}
		File.Move(SourceFilename, System.IO.Path.Combine(directoryName, newTerrainName));
		File.Delete(SourceFilename.Replace(".hdf", ".vrt"));
		foreach (KeyValuePair<string, string> tiffFilenameMap4 in tiffFilenameMap)
		{
			File.Move(System.IO.Path.Combine(directoryName, tiffFilenameMap4.Key), System.IO.Path.Combine(directoryName, tiffFilenameMap4.Value));
		}
	}

	public override bool AllSourceFilesExist()
	{
		if (!File.Exists(SourceFilename))
		{
			return false;
		}
		RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
		for (int i = 0; i < rasterFilesInfo.Length; i = checked(i + 1))
		{
			if (!File.Exists(rasterFilesInfo[i].Filename))
			{
				return false;
			}
		}
		return true;
	}

	public override List<string> GetAllSourceFiles()
	{
		List<string> allSourceFiles = new List<string>();
		allSourceFiles.Add(SourceFilename);
		allSourceFiles.Add(VRTFilename);
		RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
		foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
		{
			allSourceFiles.Add(rasterFileInfo.Filename);
		}
		return allSourceFiles;
	}

	public override List<string> GetAllVirtualizingFiles()
	{
		List<string> allVirtualizingFiles = base.GetAllVirtualizingFiles();
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			if (h5Reader.PathIsExternalLink("Terrain"))
			{
				RasterFileInfo[] rasterFilesInfo = RasterFilesInfo;
				foreach (RasterFileInfo rasterFileInfo in rasterFilesInfo)
				{
					allVirtualizingFiles.Remove(rasterFileInfo.Filename);
				}
			}
		}
		return allVirtualizingFiles;
	}

	public override List<Layer> GetLayersThatDependOnMe()
	{
		List<Layer> layers = new List<Layer>();
		if (SharedData.RasMapper != null)
		{
			TerrainsGroup terrainsGroup = SharedData.RasMapper.TerrainsGroup;
			foreach (TerrainLayer node in terrainsGroup.Nodes)
			{
				if (node == this || layers.Contains(node))
				{
					continue;
				}
				using H5Reader h5Reader = new H5Reader(node.SourceFilename);
				string fileName = "";
				string dataSet = "";
				if (h5Reader.TryGetExternalLinkValue("Terrain", out fileName, out dataSet) && Operators.CompareString(fileName, System.IO.Path.GetFileName(SourceFilename), TextCompare: false) == 0)
				{
					layers.Add(node);
					layers.AddRange(node.GetLayersThatDependOnMe());
				}
			}
		}
		return layers;
	}

	public override List<string> GetDependencyFiles()
	{
		HashSet<string> toHashSet = base.GetDependencyFiles().ToHashSet();
		using (H5Reader h5Reader = new H5Reader(SourceFilename))
		{
			string fileName = "";
			string dataSet = "";
			if (h5Reader.TryGetExternalLinkValue("Terrain", out fileName, out dataSet))
			{
				string currentDirectory = Directory.GetCurrentDirectory();
				Directory.SetCurrentDirectory(System.IO.Path.GetDirectoryName(SourceFilename));
				string fullPath = System.IO.Path.GetFullPath(fileName);
				Directory.SetCurrentDirectory(currentDirectory);
				if (File.Exists(fullPath))
				{
					TerrainLayer terrainLayer = new TerrainLayer("Terrain", fullPath);
					HashSetExtensionMethods.AddRange(toHashSet, terrainLayer.GetDependencyFiles());
				}
			}
		}
		return toHashSet.ToList();
	}

	public Histogram SurfaceHistogram()
	{
		if (_histogram == null && _rasterFilesInfo == null)
		{
			ReadRasterFilesInfo();
		}
		return _histogram;
	}

	public static string TiffFileRoundingPrecision(string filename)
	{
		string result = "(na)";
		if (Operators.CompareString(System.IO.Path.GetExtension(filename).ToLower(), ".tif", TextCompare: false) == 0)
		{
			if (TiffBase.IsBigTiff(filename))
			{
				return result;
			}
			using FloatTiffReader floatTiffReader = new FloatTiffReader(filename);
			try
			{
				return TiffBase.GetDescription(floatTiffReader.GetTiffMetadata(0).Rounding);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}
}
