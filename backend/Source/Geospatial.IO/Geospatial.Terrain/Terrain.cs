using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Geospatial.Vectors;
using H5Assist;
using TiffBinaryReader;
using Utility;
using Utility.Memory;
using Utility.Progress;

namespace Geospatial.Terrain;

public static class Terrain
{
	private struct Segment
	{
		public double x1;

		public double x2;

		public double y1;

		public double y2;

		public double z1;

		public double z2;

		public double cellSize;
	}

	public const int StitchNumColumns = 7;

	public const int StitchRowsPerChunk = 25000;

	public const byte NODATABYTE = 0;

	public const byte DATABYTE = 1;

	public const byte BOTTOM = 2;

	public const byte RIGHT = 4;

	public const byte LEFT = 8;

	public const byte TOP = 16;

	public const string FILE_NAME = "File";

	public const string FILE_CREATION = "File Creation";

	public const string FILE_LAST_ACCESS = "File Last-Access";

	public const string FILE_LAST_WRITE = "File Last-Write";

	public const string FILE_PRIORITY = "Priority";

	public const string H5_MIN = "Minimum Cell-Value";

	public const string H5_MAX = "Maximum Cell-Value";

	public const string H5_MEAN = "Mean";

	public const string H5_STDDEV = "Standard Deviation";

	private const string TERRAIN_PREFIX = "/Terrain/";

	private const string STITCH_SUFFIX = "Stitches";

	private const string STITCHTIN_SUFFIX = "Stitch TIN";

	private const string PERIMITER_SUFFIX = "/Perimeter";

	private const string MASK_SUFFIX = "/Mask";

	private const string MAXMIN_SUFFIX = "/Min-Max";

	public static void GenerateTerrain(string HDF5_Filename, List<string> TiffFiles, ProgressReporter reporter = null, bool writeStitchInformation = true)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		File.Delete(HDF5_Filename);
		using (H5Writer h5Writer = new H5Writer(HDF5_Filename))
		{
			h5Writer.CreateGroup("/Terrain/");
			List<Raster> rasters = new List<Raster>();
			int tileActionsProgress = 0;
			int allTiles = 0;
			int baseTiles = 0;
			byte[][][] byteValues = new byte[TiffFiles.Count][][];
			foreach (string TiffFile in TiffFiles)
			{
				int count = -1;
				using TiffFile tiffFile = new TiffFile(TiffFile);
				count = tiffFile.Directories.Count;
				baseTiles += tiffFile.Directories[0].Metadata.TileCount;
				for (int i = 0; i < count; i++)
				{
					allTiles += tiffFile.Directories[i].Metadata.TileCount;
				}
			}
			int prevProgress = int.MinValue;
			Action<int> action = (int numTiles) =>
			{
				int interlockedTileActionsProgressTerm = Interlocked.Add(ref tileActionsProgress, numTiles);
				int prevProgress2 = (int)Math.Min(100.0, (double)(100 * interlockedTileActionsProgressTerm) / (double)(3 * allTiles + baseTiles));
				if (prevProgress2 > prevProgress)
				{
					prevProgress = prevProgress2;
					reporter.ReportProgress(prevProgress2);
				}
			};
			Stopwatch stopwatch = new Stopwatch();
			Stopwatch stopwatch2 = new Stopwatch();
			for (int intervalIndex = 0; intervalIndex < TiffFiles.Count; intervalIndex++)
			{
				int filePriority = intervalIndex;
				string tiffFile3 = TiffFiles[intervalIndex];
				int count2 = -1;
				using (TiffFile tiffFile2 = new TiffFile(tiffFile3))
				{
					count2 = tiffFile2.Directories.Count;
					int[] intValues = new int[count2];
					for (int intervalIndex2 = 0; intervalIndex2 < count2; intervalIndex2++)
					{
						intValues[intervalIndex2] = tiffFile2.Directories[intervalIndex2].Metadata.TileCount;
					}
				}
				Raster raster = new Raster(h5Writer, tiffFile3, rasters, intervalIndex);
				for (int ordinal = 0; ordinal < count2; ordinal++)
				{
					stopwatch.Start();
					StoreMetadataParallel(h5Writer, tiffFile3, filePriority, ordinal, action, reporter);
					stopwatch.Stop();
					if (reporter.CancelRequested)
					{
						raster.Dispose();
						{
							foreach (Raster raster2 in rasters)
							{
								raster2.Dispose();
							}
							return;
						}
					}
					h5Writer.Flush();
					stopwatch2.Start();
					raster.CreatePrioritizedRenderblockOnOffMask(ordinal, byteValues, action, reporter);
					stopwatch2.Stop();
					if (!reporter.CancelRequested)
					{
						continue;
					}
					raster.Dispose();
					{
						foreach (Raster raster3 in rasters)
						{
							raster3.Dispose();
						}
						return;
					}
				}
				rasters.Add(raster);
			}
			Stopwatch stopwatch3 = Stopwatch.StartNew();
			WriteAllMaskPerimeterData(rasters, byteValues, action, reporter);
			stopwatch3.Stop();
			if (reporter.CancelRequested)
			{
				{
					foreach (Raster raster4 in rasters)
					{
						raster4.Dispose();
					}
					return;
				}
			}
			Stopwatch stopwatch4 = Stopwatch.StartNew();
			if (writeStitchInformation)
			{
				SnitchesWriteStitchesParallel(h5Writer, rasters, byteValues, action, reporter);
			}
			stopwatch4.Stop();
			foreach (Raster raster5 in rasters)
			{
				raster5.Dispose();
			}
			h5Writer.WriteAttribute("/Terrain/", "GUID", Guid.NewGuid().ToString());
		}
		reporter.ReportProgress(100);
	}

	public static void ExportMasksToBIL(string TerrainHDF5File, List<string> Tiffs, string outdir)
	{
		if (!Directory.Exists(outdir))
		{
			Directory.CreateDirectory(outdir);
		}
		using H5Writer writer = new H5Writer(TerrainHDF5File);
		List<Raster> rasters = new List<Raster>();
		int ordinal = 0;
		foreach (string Tiff in Tiffs)
		{
			rasters.Add(new Raster(writer, Tiff, null, ordinal++));
		}
		foreach (Raster raster in rasters)
		{
			raster.GetTiffName();
			int numDirectories = raster.GetNumDirectories();
			for (int i = 0; i < numDirectories; i++)
			{
				string path = Path.ChangeExtension(raster.GetTiffName(), "level" + i + "_mask.bil");
				path = outdir + Path.DirectorySeparatorChar + Path.GetFileName(path);
				raster.ExportMaskToBIL(i, path);
			}
		}
	}

	private static void StoreMetadataParallel(H5Writer h5w, string tiffFileIn, int filePriority, int pyramidLevel, Action<int> ReportTilesDone, ProgressReporter reporter)
	{
		string relativePath = MakeRelativePath(Path.GetDirectoryName(h5w.Filename) + Path.DirectorySeparatorChar, tiffFileIn);
		string perimeterDsetName = GetPerimeterName(tiffFileIn, pyramidLevel);
		string minMaxName = GetMinMaxName(tiffFileIn, pyramidLevel);
		string tiffGroupName = GetTiffGroupName(tiffFileIn);
		string pyramidGroupName = GetPyramidGroupName(tiffFileIn, pyramidLevel);
		if (!File.Exists(tiffFileIn))
		{
			throw new Exception("Input filename " + tiffFileIn + " not found.");
		}
		TiffFile ftr = new TiffFile(tiffFileIn);
		try
		{
			TagMetadata metadata = ftr.Directories[pyramidLevel].Metadata;
			int numTilesTall = metadata.TilesTall;
			int numTilesWide = metadata.TilesWide;
			int tileHeight = metadata.TileHeight;
			int tileWidth = metadata.TileWidth;
			_ = metadata.ImageWidth;
			_ = metadata.ImageHeight;
			if (!float.TryParse(metadata.NoData, NumberStyles.Float, CultureInfo.InvariantCulture, out var noDataValue))
			{
				noDataValue = float.NaN;
			}
			int count = numTilesTall * numTilesWide;
			if (tileHeight != tileWidth)
			{
				throw new Exception("Only square tiles supported at this time. Your tif " + tiffFileIn + " is incompatible.");
			}
			float[,] maxmin = new float[count, 5];
			h5w.Create2dExtendibleDataset(perimeterDsetName, 1, tileWidth + tileHeight + 1, 0f);
			Perimeter[] perimeters = new Perimeter[count];
			object[] perimeterLocks = new object[count];
			for (int i = 0; i < count; i++)
			{
				perimeterLocks[i] = new object();
			}
			object h5Lock = new object();
			float[] nodataTile = new float[tileWidth * tileHeight];
			for (int j = 0; j < nodataTile.Length; j++)
			{
				nodataTile[j] = noDataValue;
			}
			float[] nodataRow = new float[tileWidth];
			for (int k = 0; k < nodataRow.Length; k++)
			{
				nodataRow[k] = noDataValue;
			}
			Action<int> InsertPerimeterIfDNE = (int idx) =>
			{
				if (perimeters[idx] != null)
				{
					return;
				}
				lock (perimeterLocks[idx])
				{
					if (perimeters[idx] == null)
					{
						perimeters[idx] = new Perimeter();
					}
				}
			};
			Action<int> PushIfComplete = (int idx) =>
			{
				Perimeter perimeter = perimeters[idx];
				if (perimeter == null || !perimeter.IsValid())
				{
					return;
				}
				lock (perimeterLocks[idx])
				{
					if (perimeters[idx] == null)
					{
						return;
					}
					perimeters[idx] = null;
					perimeter.GetMaxMin(noDataValue, ref maxmin[idx, 3], ref maxmin[idx, 2]);
					int tileWidthTileHeightTerm = tileWidth + tileHeight + 1;
					using IMemoryOwner<float> memoryOwner = SharedMemoryPool<float>.Rent(tileWidthTileHeightTerm);
					Memory<float> slice = memoryOwner.Memory.Slice(0, tileWidthTileHeightTerm);
					perimeter.Concatenate(slice);
					lock (h5Lock)
					{
						h5w.AddRow(perimeterDsetName, slice, idx);
					}
				}
			};
			ConcurrentBag<Stats> allStats = new ConcurrentBag<Stats>();
			int rowCounter = 0;
			ParallelOptions parallelOptions = new ParallelOptions();
			parallelOptions.MaxDegreeOfParallelism = 16;
			System.Threading.Tasks.Parallel.For(0, numTilesTall, parallelOptions, (int _) =>
			{
				int incrementInterlockedTerm = Interlocked.Increment(ref rowCounter) - 1;
				bool numTilesTallTerm = incrementInterlockedTerm == numTilesTall - 1;
				bool condition = incrementInterlockedTerm == 0;
				List<Stats> statsList = new List<Stats>();
				float[] floatValues = new float[tileWidth * tileHeight];
				for (int l = 0; l < numTilesWide; l++)
				{
					if (reporter.CancelRequested)
					{
						return;
					}
					int numTilesWideTerm = incrementInterlockedTerm * numTilesWide + l;
					ftr.Directories[pyramidLevel].GetTile<float>(numTilesWideTerm, floatValues);
					bool condition2 = l == 0;
					bool numTilesWideTerm2 = l == numTilesWide - 1;
					bool condition3 = !condition && !condition2;
					bool condition4 = !numTilesTallTerm && !numTilesWideTerm2;
					bool condition5 = !condition;
					bool condition6 = !condition2;
					bool condition7 = !numTilesWideTerm2;
					bool condition8 = !numTilesTallTerm;
					int numTilesWideTerm3 = (incrementInterlockedTerm - 1) * numTilesWide + (l - 1);
					int numTilesWideTerm4 = (incrementInterlockedTerm - 1) * numTilesWide + l;
					int numTilesWideTerm5 = incrementInterlockedTerm * numTilesWide + (l - 1);
					ftr.Directories[pyramidLevel].GetValidRegion(numTilesWideTerm, out var validWidth, out var validHeight);
					Stats stats2 = Stats.Compute(floatValues, noDataValue, validWidth, validHeight, tileWidth, tileHeight);
					statsList.Add(stats2);
					maxmin[numTilesWideTerm, 0] = (float)stats2.Min;
					maxmin[numTilesWideTerm, 1] = (float)stats2.Max;
					maxmin[numTilesWideTerm, 4] = (float)stats2.FractionNoData;
					if (condition6)
					{
						int ordinal = numTilesWideTerm5;
						InsertPerimeterIfDNE(ordinal);
						perimeters[ordinal].Right = RasterData<float>.GetLeft(floatValues, tileWidth, tileHeight);
						PushIfComplete(ordinal);
					}
					if (!condition7)
					{
						int ordinal2 = numTilesWideTerm;
						InsertPerimeterIfDNE(ordinal2);
						perimeters[ordinal2].Right = nodataRow;
						PushIfComplete(ordinal2);
					}
					if (condition5)
					{
						int ordinal3 = numTilesWideTerm4;
						InsertPerimeterIfDNE(ordinal3);
						perimeters[ordinal3].Bottom = RasterData<float>.GetTop(floatValues, tileWidth).ToArray();
						PushIfComplete(ordinal3);
					}
					if (!condition8)
					{
						int ordinal4 = numTilesWideTerm;
						InsertPerimeterIfDNE(ordinal4);
						perimeters[ordinal4].Bottom = nodataRow;
						PushIfComplete(ordinal4);
					}
					if (condition3)
					{
						int ordinal5 = numTilesWideTerm3;
						InsertPerimeterIfDNE(ordinal5);
						perimeters[ordinal5].LowerRight = floatValues[0];
						PushIfComplete(ordinal5);
					}
					if (!condition4)
					{
						int ordinal6 = numTilesWideTerm;
						InsertPerimeterIfDNE(ordinal6);
						perimeters[ordinal6].LowerRight = nodataTile[tileWidth * tileHeight - 1];
						PushIfComplete(ordinal6);
					}
				}
				allStats.Add(Stats.Combine(statsList));
				ReportTilesDone(numTilesWide);
			});
			for (int intervalIndex = 0; intervalIndex < perimeters.Length; intervalIndex++)
			{
				if (perimeters[intervalIndex] != null)
				{
					throw new Exception();
				}
			}
			perimeters = null;
			float[,] floatValues2 = new float[count, 3];
			for (int intervalIndex2 = 0; intervalIndex2 < maxmin.GetLength(0); intervalIndex2++)
			{
				if (maxmin[intervalIndex2, 0] == noDataValue || float.IsNaN(maxmin[intervalIndex2, 0]))
				{
					floatValues2[intervalIndex2, 0] = maxmin[intervalIndex2, 2];
					floatValues2[intervalIndex2, 1] = maxmin[intervalIndex2, 3];
				}
				else if (maxmin[intervalIndex2, 2] == noDataValue || float.IsNaN(maxmin[intervalIndex2, 2]))
				{
					floatValues2[intervalIndex2, 0] = maxmin[intervalIndex2, 0];
					floatValues2[intervalIndex2, 1] = maxmin[intervalIndex2, 1];
				}
				else
				{
					floatValues2[intervalIndex2, 0] = Math.Min(maxmin[intervalIndex2, 0], maxmin[intervalIndex2, 2]);
					floatValues2[intervalIndex2, 1] = Math.Max(maxmin[intervalIndex2, 1], maxmin[intervalIndex2, 3]);
				}
				floatValues2[intervalIndex2, 2] = maxmin[intervalIndex2, 4];
			}
			h5w.WriteDataset(minMaxName, floatValues2);
			h5w.WriteAttribute(perimeterDsetName, "Description", "The Nth row of data represents the perimeter of the Nth tile for this dataset. This row will be a list of float values of length [Tile-Height + Tile-Width + 1]. It will be arranged like this: [Right Edge, Bottom Edge, Lower-Right Corner].");
			h5w.WriteAttribute(minMaxName, "Row N", "Tile N in this Dataset.");
			h5w.WriteAttribute(minMaxName, "Columns", new string[3] { "Minimum Tile Value (With LR Perim)", "Maximum Tile Value (With LR Perim)", "Tile Fraction Nodata" });
			Stats stats = Stats.Combine(allStats.ToList());
			h5w.WriteAttribute(pyramidGroupName, "Minimum Cell-Value", stats.Min);
			h5w.WriteAttribute(pyramidGroupName, "Maximum Cell-Value", stats.Max);
			h5w.WriteAttribute(pyramidGroupName, "Mean", stats.Mean);
			h5w.WriteAttribute(pyramidGroupName, "Standard Deviation", stats.StdDev);
			string textValue = "yyyyMMMdd HH:mm:ss";
			h5w.WriteAttribute(tiffGroupName, "File", relativePath);
			h5w.WriteAttribute(tiffGroupName, "File Creation", File.GetCreationTime(tiffFileIn).ToString(textValue));
			h5w.WriteAttribute(tiffGroupName, "File Last-Access", File.GetLastAccessTime(tiffFileIn).ToString(textValue));
			h5w.WriteAttribute(tiffGroupName, "File Last-Write", File.GetLastWriteTime(tiffFileIn).ToString(textValue));
			h5w.WriteAttribute(tiffGroupName, "Priority", filePriority);
		}
		finally
		{
			if (ftr != null)
			{
				((IDisposable)ftr).Dispose();
			}
		}
	}

	private static void SnitchesWriteStitchesParallel(H5Writer h5w, List<Raster> HigherPriorityRasters, byte[][][] rasterPyramidRBTileStats, Action<int> ReportTilesDone, ProgressReporter reporter)
	{
		string stitchesName = GetStitchesName();
		if (h5w.ExistsAsDataset(stitchesName))
		{
			h5w.Delete(stitchesName);
		}
		h5w.Create2dExtendibleDataset(stitchesName, 25000, 7, 0.0);
		List<Segment> segments = new List<Segment>();
		foreach (Raster r in HigherPriorityRasters)
		{
			byte[] tileStats = rasterPyramidRBTileStats[r.RasterPriorityLevel][0];
			r.GetNumTiles(0);
			int numTilesTall = r.GetNumTilesTall(0);
			int numTilesWide = r.GetNumTilesWide(0);
			r.GetNumRows(0);
			r.GetNumCols(0);
			int tileHeight = r.GetTileHeight(0);
			int tileWidth = r.GetTileWidth(0);
			string tiffName = r.GetTiffName();
			Extent rasterCorners = r.GetCorners();
			double cellSize = r.GetCellSize(0);
			List<Segment>[] segmentTileBuffer = new List<Segment>[numTilesTall];
			int activeRow = 0;
			ParallelOptions parallelOptions = new ParallelOptions();
			parallelOptions.MaxDegreeOfParallelism = 16;
			System.Threading.Tasks.Parallel.For(0, numTilesTall, parallelOptions, (int _) =>
			{
				int incrementInterlockedTerm = Interlocked.Increment(ref activeRow) - 1;
				List<Segment> rowStitches = new List<Segment>();
				for (int i = 0; i < numTilesWide; i++)
				{
					if (reporter.CancelRequested)
					{
						return;
					}
					int numTilesWideTerm = incrementInterlockedTerm * numTilesWide + i;
					if (tileStats[numTilesWideTerm] != 0 && tileStats[numTilesWideTerm] != 129)
					{
						double tileLeft = rasterCorners.LowerLeft.X + (double)(i * tileWidth) * cellSize;
						double tileTop = rasterCorners.UpperLeft.Y - (double)(incrementInterlockedTerm * tileHeight) * cellSize;
						float[] tile = r.GetTile(numTilesWideTerm, 0);
						float[,] perimValues = null;
						h5w.ReadRows(GetPerimeterName(tiffName, 0), numTilesWideTerm, 1, ref perimValues);
						byte[,] mask = null;
						h5w.ReadRows(GetMaskName(tiffName, 0), numTilesWideTerm, 1, ref mask);
						Action<int, int> action = (int row, int col) =>
						{
							int rowTileWidthTerm = row * tileWidth + col;
							byte b = mask[0, rowTileWidthTerm];
							if (b != 1 && (b & 1) != 0)
							{
								float tile2 = tile[rowTileWidthTerm];
								float perimValuesRowTerm;
								float perimValuesColTerm;
								float perimValuesColTerm2;
								if (col == tileWidth - 1)
								{
									if (row == tileHeight - 1)
									{
										perimValuesRowTerm = perimValues[0, row];
										perimValuesColTerm = perimValues[0, col + tileWidth + 1];
										perimValuesColTerm2 = perimValues[0, col + tileWidth];
									}
									else
									{
										perimValuesRowTerm = perimValues[0, row];
										perimValuesColTerm = perimValues[0, row + 1];
										perimValuesColTerm2 = tile[rowTileWidthTerm + tileWidth];
									}
								}
								else if (row == tileHeight - 1)
								{
									perimValuesRowTerm = tile[rowTileWidthTerm + 1];
									perimValuesColTerm = perimValues[0, col + tileHeight + 1];
									perimValuesColTerm2 = perimValues[0, col + tileHeight];
								}
								else
								{
									perimValuesRowTerm = tile[rowTileWidthTerm + 1];
									perimValuesColTerm = tile[rowTileWidthTerm + tileWidth + 1];
									perimValuesColTerm2 = tile[rowTileWidthTerm + tileWidth];
								}
								Point point = new Point(tileLeft + ((double)col + 0.5) * cellSize, tileTop - ((double)row + 0.5) * cellSize);
								Point point2 = new Point(tileLeft + ((double)col + 1.5) * cellSize, tileTop - ((double)row + 0.5) * cellSize);
								Point point3 = new Point(tileLeft + ((double)col + 1.5) * cellSize, tileTop - ((double)row + 1.5) * cellSize);
								Point point4 = new Point(tileLeft + ((double)col + 0.5) * cellSize, tileTop - ((double)row + 1.5) * cellSize);
								if ((b & 8) != 0)
								{
									rowStitches.Add(new Segment
									{
										x1 = point4.X,
										y1 = point4.Y,
										z1 = perimValuesColTerm2,
										x2 = point.X,
										y2 = point.Y,
										z2 = tile2,
										cellSize = cellSize
									});
								}
								if ((b & 0x10) != 0)
								{
									rowStitches.Add(new Segment
									{
										x1 = point.X,
										y1 = point.Y,
										z1 = tile2,
										x2 = point2.X,
										y2 = point2.Y,
										z2 = perimValuesRowTerm,
										cellSize = cellSize
									});
								}
								if ((b & 4) != 0)
								{
									rowStitches.Add(new Segment
									{
										x1 = point2.X,
										y1 = point2.Y,
										z1 = perimValuesRowTerm,
										x2 = point3.X,
										y2 = point3.Y,
										z2 = perimValuesColTerm,
										cellSize = cellSize
									});
								}
								if ((b & 2) != 0)
								{
									rowStitches.Add(new Segment
									{
										x1 = point3.X,
										y1 = point3.Y,
										z1 = perimValuesColTerm,
										x2 = point4.X,
										y2 = point4.Y,
										z2 = perimValuesColTerm2,
										cellSize = cellSize
									});
								}
							}
						};
						if (tileStats[numTilesWideTerm] == 1)
						{
							for (int ordinal = 0; ordinal < tileHeight; ordinal++)
							{
								action(ordinal, 0);
								action(ordinal, tileWidth - 1);
							}
							for (int ordinal2 = 0; ordinal2 < tileWidth; ordinal2++)
							{
								action(0, ordinal2);
								action(tileHeight - 1, ordinal2);
							}
						}
						else
						{
							for (int ordinal3 = 0; ordinal3 < tileHeight; ordinal3++)
							{
								for (int ordinal4 = 0; ordinal4 < tileWidth; ordinal4++)
								{
									action(ordinal3, ordinal4);
								}
							}
						}
					}
				}
				segmentTileBuffer[incrementInterlockedTerm] = rowStitches;
				ReportTilesDone(numTilesWide);
			});
			List<Segment>[] segmentTileBufferList = segmentTileBuffer;
			foreach (List<Segment> collection in segmentTileBufferList)
			{
				segments.AddRange(collection);
			}
		}
		double[,] doubleValues = new double[25000, 7];
		int count = segments.Count / 25000;
		for (int ordinal5 = 0; ordinal5 < count; ordinal5++)
		{
			int ordinal6 = ordinal5 * 25000;
			for (int intervalIndex = 0; intervalIndex < 25000; intervalIndex++)
			{
				int index = ordinal6 + intervalIndex;
				doubleValues[intervalIndex, 0] = segments[index].x1;
				doubleValues[intervalIndex, 1] = segments[index].y1;
				doubleValues[intervalIndex, 2] = segments[index].z1;
				doubleValues[intervalIndex, 3] = segments[index].x2;
				doubleValues[intervalIndex, 4] = segments[index].y2;
				doubleValues[intervalIndex, 5] = segments[index].z2;
				doubleValues[intervalIndex, 6] = segments[index].cellSize;
			}
			h5w.AddRows(stitchesName, doubleValues);
		}
		int scaledCount = count * 25000;
		doubleValues = new double[segments.Count - scaledCount, 7];
		for (int scaledCount2 = scaledCount; scaledCount2 < segments.Count; scaledCount2++)
		{
			doubleValues[scaledCount2 - scaledCount, 0] = segments[scaledCount2].x1;
			doubleValues[scaledCount2 - scaledCount, 1] = segments[scaledCount2].y1;
			doubleValues[scaledCount2 - scaledCount, 2] = segments[scaledCount2].z1;
			doubleValues[scaledCount2 - scaledCount, 3] = segments[scaledCount2].x2;
			doubleValues[scaledCount2 - scaledCount, 4] = segments[scaledCount2].y2;
			doubleValues[scaledCount2 - scaledCount, 5] = segments[scaledCount2].z2;
			doubleValues[scaledCount2 - scaledCount, 6] = segments[scaledCount2].cellSize;
		}
		h5w.AddRows(stitchesName, doubleValues);
	}

	private static void WriteAllMaskPerimeterData(List<Raster> HigherPriorityRasters, byte[][][] rasterPyramidTileStats, Action<int> ReportTiles, ProgressReporter reporter)
	{
		foreach (Raster HigherPriorityRaster in HigherPriorityRasters)
		{
			HigherPriorityRaster.ConvertOnOffMaskToPerimeterMaskParallel(rasterPyramidTileStats[HigherPriorityRaster.RasterPriorityLevel], ReportTiles, reporter);
			if (reporter.CancelRequested)
			{
				break;
			}
		}
	}

	public static string MakeRelativePath(string fromPath, string toPath)
	{
		if (string.IsNullOrEmpty(fromPath))
		{
			throw new ArgumentNullException("fromPath");
		}
		if (string.IsNullOrEmpty(toPath))
		{
			throw new ArgumentNullException("toPath");
		}
		Uri uri = new Uri(fromPath);
		Uri uri2 = new Uri(toPath);
		if (uri.Scheme != uri2.Scheme)
		{
			return toPath;
		}
		string unescapeDataString = Uri.UnescapeDataString(uri.MakeRelativeUri(uri2).ToString());
		if (uri2.Scheme.ToUpperInvariant() == "FILE")
		{
			unescapeDataString = unescapeDataString.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		}
		return unescapeDataString;
	}

	public static string GetStitchesName()
	{
		return "/Terrain/Stitches";
	}

	public static string GetStitchTinName()
	{
		return "/Terrain/Stitch TIN";
	}

	public static string GetPerimeterName(string TiffFileIn, int PyramidLevel)
	{
		return GetPyramidGroupName(TiffFileIn, PyramidLevel) + "/Perimeter";
	}

	public static string GetMaskName(string TiffFileIn, int PyramidLevel)
	{
		return GetPyramidGroupName(TiffFileIn, PyramidLevel) + "/Mask";
	}

	public static string GetMinMaxName(string TiffFileIn, int PyramidLevel)
	{
		return GetPyramidGroupName(TiffFileIn, PyramidLevel) + "/Min-Max";
	}

	internal static string GetTiffGroupName(string TiffFileIn)
	{
		return "/Terrain/" + Path.GetFileNameWithoutExtension(TiffFileIn);
	}

	internal static string GetPyramidGroupName(string TiffFileIn, int PyramidLevel)
	{
		return GetTiffGroupName(TiffFileIn) + "/" + PyramidLevel;
	}
}
