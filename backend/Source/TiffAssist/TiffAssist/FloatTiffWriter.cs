using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using BitMiracle.LibTiff.Classic;
using Geospatial.Rasters;
using Utility;
using Utility.Progress;

namespace TiffAssist;

public class FloatTiffWriter : TiffWriter<float>
{
	private Func<float, float> ROUND;

	private const string NColsHDRTag = "ncols";

	private const string NRowsHDRTag = "nrows";

	private const string ByteOrderHDRTag = "byteorder";

	private const string NoDataHDRTag = "NODATA_value";

	private const string GdalMetadataTagName = "GDALMetadata";

	public static void CopyData(string srcFilename, string destFilename, TiffMetadata<float> destMetadata, float verticalStretchFactor, ProgressReporter reporter, string appendArgs = "")
	{
		File.Delete(destFilename);
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		using FloatTiffReader floatTiffReader = new FloatTiffReader(srcFilename);
		int numTilesTall = floatTiffReader.GetNumTilesTall(0);
		int numTilesWide = floatTiffReader.GetNumTilesWide(0);
		int tileHeight = floatTiffReader.GetTileHeight(0);
		int tileWidth = floatTiffReader.GetTileWidth(0);
		int imageWidth = floatTiffReader.GetImageWidth(0);
		int imageHeight = floatTiffReader.GetImageHeight(0);
		float noData = floatTiffReader.GetNoData(0);
		if (destMetadata.TileWidth != tileWidth || destMetadata.TileHeight != tileHeight)
		{
			throw new NotImplementedException("Converting to a file with a different tile size is not currently supported.");
		}
		using FloatTiffWriter floatTiffWriter = new FloatTiffWriter(destFilename, imageWidth, imageHeight, ComputeStats: true, destMetadata, appendArgs);
		float[] arr = new float[floatTiffReader.GetTileHeight(0) * floatTiffReader.GetTileWidth(0)];
		for (int i = 0; i < numTilesTall; i++)
		{
			for (int j = 0; j < numTilesWide; j++)
			{
				int numTilesWideTerm = i * numTilesWide + j;
				floatTiffReader.GetTile(0, numTilesWideTerm, ref arr);
				for (int k = 0; k < arr.Length; k++)
				{
					float arr2 = arr[k];
					if (arr2 == noData || float.IsNaN(arr2) || float.IsInfinity(arr2))
					{
						arr[k] = destMetadata.NoDataValue;
					}
				}
				if (verticalStretchFactor != 1f)
				{
					for (int l = 0; l < arr.Length; l++)
					{
						if (arr[l] != destMetadata.NoDataValue)
						{
							arr[l] *= verticalStretchFactor;
						}
					}
				}
				floatTiffWriter.WriteTile(numTilesWideTerm, arr);
				if (numTilesWideTerm % 50 == 0)
				{
					if (reporter.CancelRequested)
					{
						return;
					}
					int numTilesWideTerm2 = i * numTilesWide + j;
					int prog = (int)(100f * (float)(numTilesWideTerm2 + 1) / (float)(numTilesTall * numTilesWide));
					reporter.ReportProgress(prog);
				}
			}
		}
	}

	public static void CreateFromFLT(string FLTFileName, string TiffFileName, TiffMetadata<float> Metadata)
	{
		string changeExtension = Path.ChangeExtension(FLTFileName, "hdr");
		if (!File.Exists(changeExtension))
		{
			throw new Exception("No HDR File detected for " + FLTFileName + ", expected file " + changeExtension);
		}
		string[] allLines = File.ReadAllLines(changeExtension);
		int allLineTerm = -1;
		int allLineTerm2 = -1;
		float noDataValue = Metadata.NoDataValue;
		float naN = float.NaN;
		for (int i = 0; i < allLines.Length; i++)
		{
			string allLine = allLines[i];
			if (allLine.Length >= "ncols".Length && allLine.Substring(0, "ncols".Length) == "ncols")
			{
				allLine = allLine.Substring("ncols".Length).Trim();
				allLineTerm = Convert.ToInt32(allLine);
			}
			else if (allLine.Length >= "nrows".Length && allLine.Substring(0, "nrows".Length) == "nrows")
			{
				allLine = allLine.Substring("nrows".Length).Trim();
				allLineTerm2 = Convert.ToInt32(allLine);
			}
			else if (allLine.Length >= "byteorder".Length && allLine.Substring(0, "byteorder".Length) == "byteorder")
			{
				allLine = allLine.Substring("byteorder".Length).Trim();
			}
			else if (allLine.Length >= "NODATA_value".Length && allLine.Substring(0, "NODATA_value".Length) == "NODATA_value")
			{
				allLine = allLine.Substring("NODATA_value".Length).Trim();
				naN = Convert.ToSingle(allLine);
			}
		}
		try
		{
			if (File.Exists(TiffFileName))
			{
				File.Delete(TiffFileName);
			}
		}
		catch
		{
			throw new Exception("Could not delete file " + TiffFileName);
		}
		FloatTiffWriter floatTiffWriter = new FloatTiffWriter(TiffFileName, allLineTerm, allLineTerm2, ComputeStats: true, Metadata);
		int numTilesWide = floatTiffWriter.GetNumTilesWide(0);
		int numTilesTall = floatTiffWriter.GetNumTilesTall(0);
		using (FileStream fileStream = new FileStream(FLTFileName, FileMode.Open))
		{
			for (int j = 0; j < numTilesTall; j++)
			{
				for (int k = 0; k < numTilesWide; k++)
				{
					float[] floatValues = new float[Metadata.TileWidth * Metadata.TileHeight];
					for (int l = 0; l < floatValues.Length; l++)
					{
						floatValues[l] = noDataValue;
					}
					byte[] byteValues = new byte[Metadata.TileWidth * Metadata.TileHeight * 4];
					Buffer.BlockCopy(floatValues, 0, byteValues, 0, byteValues.Length);
					for (int m = 0; m < Metadata.TileHeight && j * Metadata.TileHeight + m < allLineTerm2; m++)
					{
						int scaledTileWidth = 4 * Metadata.TileWidth;
						if (k * Metadata.TileWidth + 256 > allLineTerm)
						{
							scaledTileWidth = 4 * (allLineTerm - k * Metadata.TileWidth);
						}
						fileStream.Position = 4 * ((j * Metadata.TileHeight + m) * allLineTerm + k * Metadata.TileWidth);
						if (fileStream.Read(byteValues, m * Metadata.TileWidth * 4, scaledTileWidth) != scaledTileWidth)
						{
							throw new Exception("Unexpected EOF.");
						}
					}
					Buffer.BlockCopy(byteValues, 0, floatValues, 0, byteValues.Length);
					if (!float.IsNaN(naN) && naN != noDataValue)
					{
						for (int n = 0; n < floatValues.Length; n++)
						{
							if (floatValues[n] == naN)
							{
								floatValues[n] = noDataValue;
							}
						}
					}
					else if (float.IsNaN(naN) && !float.IsNaN(noDataValue))
					{
						for (int intervalIndex = 0; intervalIndex < floatValues.Length; intervalIndex++)
						{
							if (float.IsNaN(floatValues[intervalIndex]))
							{
								floatValues[intervalIndex] = noDataValue;
							}
						}
					}
					floatTiffWriter.WriteTile(j * numTilesWide + k, floatValues);
				}
			}
		}
		floatTiffWriter.Dispose();
	}

	public static void Write(string tiffFilename, float[,] data, TiffMetadata<float> metadata = null)
	{
		if (metadata == null)
		{
			metadata = new TiffMetadata<float>(-9999f, RoundTo.None);
		}
		int length = data.GetLength(1);
		int length2 = data.GetLength(0);
		float[,] floatValues = new float[metadata.TileHeight, metadata.TileWidth];
		for (int i = 0; i < metadata.TileHeight; i++)
		{
			for (int j = 0; j < metadata.TileWidth; j++)
			{
				floatValues[i, j] = metadata.NoDataValue;
			}
		}
		float[,] floatValues2 = new float[metadata.TileHeight, metadata.TileWidth];
		using FloatTiffWriter floatTiffWriter = new FloatTiffWriter(tiffFilename, length, length2, ComputeStats: true, metadata);
		int numTilesWide = floatTiffWriter.GetNumTilesWide(0);
		int numTilesTall = floatTiffWriter.GetNumTilesTall(0);
		for (int k = 0; k < numTilesTall; k++)
		{
			for (int l = 0; l < numTilesWide; l++)
			{
				Buffer.BlockCopy(floatValues, 0, floatValues2, 0, floatValues2.Length * 4);
				for (int m = 0; m < metadata.TileHeight; m++)
				{
					int tileHeightMetadataTerm = k * metadata.TileHeight + m;
					if (tileHeightMetadataTerm >= length2)
					{
						break;
					}
					for (int n = 0; n < metadata.TileWidth; n++)
					{
						int tileWidthMetadataTerm = l * metadata.TileWidth + n;
						if (tileWidthMetadataTerm >= length)
						{
							break;
						}
						floatValues2[m, n] = data[tileHeightMetadataTerm, tileWidthMetadataTerm];
					}
				}
				floatTiffWriter.WriteTile(k * numTilesWide + l, floatValues2);
			}
		}
	}

	public static void ComputeSetHistogram(string TiffFilename, int NumBuckets, out int[] Buckets, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Buckets = new int[NumBuckets];
		XElement xElement;
		using (FloatTiffReader floatTiffReader = new FloatTiffReader(TiffFilename))
		{
			floatTiffReader.ReadGDALStats(0, out var min, out var Maximum, out var _, out var _, out var _);
			if (double.IsNaN(min) || double.IsNaN(Maximum))
			{
				throw new Exception("Target file: " + TiffFilename + " does not have embedded statistics.");
			}
			double delta = Maximum - min;
			double increment = delta / (double)NumBuckets;
			Func<double, int> func = (double computed) =>
			{
				double minTerm = computed - min;
				if (minTerm <= 0.0)
				{
					return 0;
				}
				return (minTerm >= delta) ? (NumBuckets - 1) : ((int)Math.Floor(minTerm / increment));
			};
			int ordinal = 0;
			int numTiles = floatTiffReader.GetNumTiles(ordinal);
			int tileWidth = floatTiffReader.GetTileWidth(ordinal);
			int tileHeight = floatTiffReader.GetTileHeight(ordinal);
			float noData = floatTiffReader.GetNoData(ordinal);
			float[,] arr = new float[tileHeight, tileWidth];
			for (int ordinal2 = 0; ordinal2 < numTiles; ordinal2++)
			{
				if (reporter.CancelRequested)
				{
					return;
				}
				floatTiffReader.GetValidRegion(ordinal, ordinal2, out var ValidWidth, out var ValidHeight);
				floatTiffReader.GetTile(ordinal, ordinal2, ref arr);
				for (int intervalIndex = 0; intervalIndex < ValidHeight; intervalIndex++)
				{
					for (int intervalIndex2 = 0; intervalIndex2 < ValidWidth; intervalIndex2++)
					{
						if (arr[intervalIndex, intervalIndex2] != noData)
						{
							Buckets[func(arr[intervalIndex, intervalIndex2])]++;
						}
					}
				}
				reporter.ReportProgress((int)((float)((ordinal2 + 1) * 100) / (float)numTiles));
			}
			xElement = floatTiffReader.GetGDALXML(0);
			if (xElement == null)
			{
				xElement = new XElement("GDALMetadata");
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int intervalIndex3 = 0; intervalIndex3 < Buckets.Length; intervalIndex3++)
		{
			stringBuilder.Append(Buckets[intervalIndex3].ToString());
			if (intervalIndex3 != Buckets.Length - 1)
			{
				stringBuilder.Append("|");
			}
		}
		(from el in xElement.Elements("Item")
			where (string?)el.Attribute("name") == TiffBase.HISTOGRAM_NAME
			select el).Remove();
		XElement xElement2 = new XElement("Item");
		xElement2.SetAttributeValue("name", TiffBase.HISTOGRAM_NAME);
		xElement2.Value = stringBuilder.ToString();
		xElement.Add(xElement2);
		TiffWriterBase.SetCustomTag(TiffFilename, 0, CustomTags.GDAL_METADATA, xElement.ToString());
	}

	public static void ComputeSetHistogramPartial(string TiffFilename, int NumBuckets, out int[] Buckets, List<int> tilesToCount, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Buckets = new int[NumBuckets];
		XElement xElement;
		using (FloatTiffReader floatTiffReader = new FloatTiffReader(TiffFilename))
		{
			floatTiffReader.ReadGDALStats(0, out var min, out var Maximum, out var _, out var _, out var _);
			if (double.IsNaN(min) || double.IsNaN(Maximum))
			{
				throw new Exception("Target file: " + TiffFilename + " does not have embedded statistics.");
			}
			double delta = Maximum - min;
			double increment = delta / (double)NumBuckets;
			Func<double, int> func = (double computed) =>
			{
				double minTerm = computed - min;
				if (minTerm <= 0.0)
				{
					return 0;
				}
				return (minTerm >= delta) ? (NumBuckets - 1) : ((int)Math.Floor(minTerm / increment));
			};
			int ordinal = 0;
			int numTiles = floatTiffReader.GetNumTiles(ordinal);
			int tileWidth = floatTiffReader.GetTileWidth(ordinal);
			int tileHeight = floatTiffReader.GetTileHeight(ordinal);
			float noData = floatTiffReader.GetNoData(ordinal);
			float[,] arr = new float[tileHeight, tileWidth];
			foreach (int tilesToCount2 in tilesToCount)
			{
				if (reporter.CancelRequested)
				{
					return;
				}
				floatTiffReader.GetValidRegion(ordinal, tilesToCount2, out var ValidWidth, out var ValidHeight);
				floatTiffReader.GetTile(ordinal, tilesToCount2, ref arr);
				for (int intervalIndex = 0; intervalIndex < ValidHeight; intervalIndex++)
				{
					for (int intervalIndex2 = 0; intervalIndex2 < ValidWidth; intervalIndex2++)
					{
						if (arr[intervalIndex, intervalIndex2] != noData)
						{
							Buckets[func(arr[intervalIndex, intervalIndex2])]++;
						}
					}
				}
				reporter.ReportProgress((int)((float)((tilesToCount2 + 1) * 100) / (float)numTiles));
			}
			xElement = floatTiffReader.GetGDALXML(0);
			if (xElement == null)
			{
				xElement = new XElement("GDALMetadata");
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int intervalIndex3 = 0; intervalIndex3 < Buckets.Length; intervalIndex3++)
		{
			stringBuilder.Append(Buckets[intervalIndex3].ToString());
			if (intervalIndex3 != Buckets.Length - 1)
			{
				stringBuilder.Append("|");
			}
		}
		(from el in xElement.Elements("Item")
			where (string?)el.Attribute("name") == TiffBase.HISTOGRAM_NAME
			select el).Remove();
		XElement xElement2 = new XElement("Item");
		xElement2.SetAttributeValue("name", TiffBase.HISTOGRAM_NAME);
		xElement2.Value = stringBuilder.ToString();
		xElement.Add(xElement2);
		TiffWriterBase.SetCustomTag(TiffFilename, 0, CustomTags.GDAL_METADATA, xElement.ToString());
	}

	public static void ComputeSetHistogramParallelSimple(string TiffFilename, int NumBuckets, out int[] Buckets, ProgressReporter reporter)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		Buckets = new int[NumBuckets];
		XElement xElement;
		using (FloatTiffReader floatTiffReader = new FloatTiffReader(TiffFilename))
		{
			floatTiffReader.ReadGDALStats(0, out var min, out var Maximum, out var _, out var _, out var _);
			if (double.IsNaN(min) || double.IsNaN(Maximum))
			{
				throw new Exception("Target file: " + TiffFilename + " does not have embedded statistics.");
			}
			double delta = Maximum - min;
			double increment = delta / (double)NumBuckets;
			Func<double, int> BinIndex = (double computed) =>
			{
				double minTerm = computed - min;
				if (minTerm <= 0.0)
				{
					return 0;
				}
				return (minTerm >= delta) ? (NumBuckets - 1) : ((int)Math.Floor(minTerm / increment));
			};
			int ordinal = 0;
			int numTiles = floatTiffReader.GetNumTiles(ordinal);
			int tileWidth = floatTiffReader.GetTileWidth(ordinal);
			int tileHeight = floatTiffReader.GetTileHeight(ordinal);
			float nodata = floatTiffReader.GetNoData(ordinal);
			float[,] tile = new float[tileHeight, tileWidth];
			for (int ordinal2 = 0; ordinal2 < numTiles; ordinal2++)
			{
				if (reporter.CancelRequested)
				{
					return;
				}
				floatTiffReader.GetValidRegion(ordinal, ordinal2, out var validWidth, out var ValidHeight);
				floatTiffReader.GetTile(ordinal, ordinal2, ref tile);
				List<int[]> bucketList = new List<int[]>(ValidHeight);
				System.Threading.Tasks.Parallel.For(0, ValidHeight, (int i) =>
				{
					int[] intValues = new int[NumBuckets];
					for (int j = 0; j < validWidth; j++)
					{
						if (tile[i, j] != nodata)
						{
							intValues[BinIndex(tile[i, j])]++;
						}
					}
					bucketList.Add(intValues);
				});
				foreach (int[] bucketListArray in bucketList)
				{
					for (int intervalIndex = 0; intervalIndex < bucketListArray.Length; intervalIndex++)
					{
						Buckets[intervalIndex] += bucketListArray[intervalIndex];
					}
				}
				reporter.ReportProgress((int)((float)((ordinal2 + 1) * 100) / (float)numTiles));
			}
			xElement = floatTiffReader.GetGDALXML(0);
			if (xElement == null)
			{
				xElement = new XElement("GDALMetadata");
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int intervalIndex2 = 0; intervalIndex2 < Buckets.Length; intervalIndex2++)
		{
			stringBuilder.Append(Buckets[intervalIndex2].ToString());
			if (intervalIndex2 != Buckets.Length - 1)
			{
				stringBuilder.Append("|");
			}
		}
		(from el in xElement.Elements("Item")
			where (string?)el.Attribute("name") == TiffBase.HISTOGRAM_NAME
			select el).Remove();
		XElement xElement2 = new XElement("Item");
		xElement2.SetAttributeValue("name", TiffBase.HISTOGRAM_NAME);
		xElement2.Value = stringBuilder.ToString();
		xElement.Add(xElement2);
		TiffWriterBase.SetCustomTag(TiffFilename, 0, CustomTags.GDAL_METADATA, xElement.ToString());
	}

	public static void ComputeSetHistogramParallel(string tiffFilename, int numBuckets, out int[] buckets)
	{
		throw new NotImplementedException();
	}

	public FloatTiffWriter(string tiffFilename, int imageWidth, int imageHeight, bool ComputeStats, TiffMetadata<float> metadata, string appendArg = "", bool deleteExistingFile = true)
		: base(tiffFilename, imageWidth, imageHeight, SampleFormat.IEEEFP, ComputeStats, metadata, appendArg, deleteExistingFile)
	{
		if (float.IsNaN(metadata.NoDataValue))
		{
			throw new ArgumentException("Cannot have input value NaN for NoData.");
		}
		if (metadata.MaxReasonableValue.HasValue || metadata.MinReasonableValue.HasValue)
		{
			float replaceMin = float.MinValue;
			float replaceMax = float.MaxValue;
			if (metadata.MaxReasonableValue.HasValue)
			{
				replaceMax = metadata.MaxReasonableValue.Value;
			}
			if (metadata.MinReasonableValue.HasValue)
			{
				replaceMin = metadata.MinReasonableValue.Value;
			}
			InitializeRounding(metadata.Rounding, replaceMax, replaceMin, metadata.NoDataValue);
		}
		else
		{
			InitializeRounding(metadata.Rounding);
		}
	}

	public FloatTiffWriter(Stream stream, int imageWidth, int imageHeight, bool computeStats, TiffMetadata<float> metadata)
		: base(stream, imageWidth, imageHeight, SampleFormat.IEEEFP, computeStats, metadata)
	{
		if (float.IsNaN(metadata.NoDataValue))
		{
			throw new ArgumentException("Cannot have input value NaN for NoData.");
		}
		if (metadata.MaxReasonableValue.HasValue && metadata.MinReasonableValue.HasValue)
		{
			InitializeRounding(metadata.Rounding, metadata.MaxReasonableValue.Value, metadata.MinReasonableValue.Value, metadata.NoDataValue);
		}
		else
		{
			InitializeRounding(metadata.Rounding);
		}
	}

	private void InitializeRounding(RoundTo rounding)
	{
		switch (rounding)
		{
		case RoundTo.None:
			ROUND = null;
			break;
		case RoundTo.Tenths:
			ROUND = (float f) => (float)Math.Round(f, 1);
			break;
		case RoundTo.Hundredths:
			ROUND = (float f) => (float)Math.Round(f, 2);
			break;
		case RoundTo.Thousandths:
			ROUND = (float f) => (float)Math.Round(f, 3);
			break;
		case RoundTo.Sixteenths:
			ROUND = (float f) => (float)(Math.Round(f * 16f) / 16.0);
			break;
		case RoundTo.ThirtySeconds:
			ROUND = (float f) => (float)(Math.Round(f * 32f) / 32.0);
			break;
		case RoundTo.SixtyFourths:
			ROUND = (float f) => (float)(Math.Round(f * 64f) / 64.0);
			break;
		case RoundTo.HundredTwentyEighths:
			ROUND = (float f) => (float)(Math.Round(f * 128f) / 128.0);
			break;
		case RoundTo.ThousandTwentyFourths:
			ROUND = (float f) => (float)(Math.Round(f * 1024f) / 1024.0);
			break;
		default:
			throw new Exception("Round Type not supported.");
		}
	}

	private void InitializeRounding(RoundTo rounding, float replaceMax, float replaceMin, float noDataValue)
	{
		switch (rounding)
		{
		case RoundTo.None:
			ROUND = (float f) => (!(f > replaceMax) && !(f < replaceMin)) ? f : noDataValue;
			break;
		case RoundTo.Tenths:
			ROUND = (float f) =>
			{
				float computed = (float)Math.Round(f, 1);
				return (!(computed > replaceMax) && !(computed < replaceMin)) ? computed : noDataValue;
			};
			break;
		case RoundTo.Hundredths:
			ROUND = (float f) =>
			{
				float computed2 = (float)Math.Round(f, 2);
				return (!(computed2 > replaceMax) && !(computed2 < replaceMin)) ? computed2 : noDataValue;
			};
			break;
		case RoundTo.Thousandths:
			ROUND = (float f) =>
			{
				float computed3 = (float)Math.Round(f, 3);
				return (!(computed3 > replaceMax) && !(computed3 < replaceMin)) ? computed3 : noDataValue;
			};
			break;
		case RoundTo.Sixteenths:
			ROUND = (float f) =>
			{
				float computed4 = (float)(Math.Round(f * 16f) / 16.0);
				return (!(computed4 > replaceMax) && !(computed4 < replaceMin)) ? computed4 : noDataValue;
			};
			break;
		case RoundTo.ThirtySeconds:
			ROUND = (float f) =>
			{
				float computed5 = (float)(Math.Round(f * 32f) / 32.0);
				return (!(computed5 > replaceMax) && !(computed5 < replaceMin)) ? computed5 : noDataValue;
			};
			break;
		case RoundTo.SixtyFourths:
			ROUND = (float f) =>
			{
				float computed6 = (float)(Math.Round(f * 64f) / 64.0);
				return (!(computed6 > replaceMax) && !(computed6 < replaceMin)) ? computed6 : noDataValue;
			};
			break;
		case RoundTo.HundredTwentyEighths:
			ROUND = (float f) =>
			{
				float computed7 = (float)(Math.Round(f * 128f) / 128.0);
				return (!(computed7 > replaceMax) && !(computed7 < replaceMin)) ? computed7 : noDataValue;
			};
			break;
		case RoundTo.ThousandTwentyFourths:
			ROUND = (float f) =>
			{
				float computed8 = (float)(Math.Round(f * 1024f) / 1024.0);
				return (!(computed8 > replaceMax) && !(computed8 < replaceMin)) ? computed8 : noDataValue;
			};
			break;
		default:
			throw new Exception("Round Type not supported.");
		}
	}

	public override void WriteTile(int TileNumber, float[] data)
	{
		if (TrackStats)
		{
			Stats tileStats = RoundTileWithStats(data, TileNumber);
			TileStats[TileNumber] = tileStats;
		}
		else
		{
			RoundTileNoStats(data, TileNumber);
		}
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(TileNumber, byteValues);
	}

	public override void WriteTile(int TileNumber, float[,] data)
	{
		if (TrackStats)
		{
			Stats tileStats = RoundTileWithStats(data, TileNumber);
			TileStats[TileNumber] = tileStats;
		}
		else
		{
			RoundTileNoStats(data, TileNumber);
		}
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(TileNumber, byteValues);
	}

	private void RoundTileNoStats(float[] tile, int TileNumber)
	{
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		int actualWidth = Metadata.TileWidth;
		if (ROUND == null)
		{
			return;
		}
		float noData = Metadata.NoDataValue;
		Action<int> body = (int i) =>
		{
			for (int j = 0; j < validWidth; j++)
			{
				int actualWidthTerm = i * actualWidth + j;
				if (tile[actualWidthTerm] != noData)
				{
					tile[actualWidthTerm] = ROUND(tile[actualWidthTerm]);
				}
			}
		};
		System.Threading.Tasks.Parallel.For(0, validHeight, body);
	}

	private void RoundTileNoStats(float[,] tile, int TileNumber)
	{
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		_ = Metadata.TileWidth;
		if (ROUND == null)
		{
			return;
		}
		float noData = Metadata.NoDataValue;
		Action<int> body = (int i) =>
		{
			for (int j = 0; j < validWidth; j++)
			{
				if (tile[i, j] != noData)
				{
					tile[i, j] = ROUND(tile[i, j]);
				}
			}
		};
		System.Threading.Tasks.Parallel.For(0, validHeight, body);
	}

	private Stats RoundTileWithStats(float[] tile, int TileNumber)
	{
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		int actualWidth = Metadata.TileWidth;
		ConcurrentBag<Stats> rows = new ConcurrentBag<Stats>();
		Action<int> body = (int i) =>
		{
			float noDataValue = Metadata.NoDataValue;
			Stats stats = new Stats(noDataValue);
			for (int j = 0; j < validWidth; j++)
			{
				int actualWidthTerm = i * actualWidth + j;
				if (ROUND != null && tile[actualWidthTerm] != noDataValue)
				{
					tile[actualWidthTerm] = ROUND(tile[actualWidthTerm]);
				}
			}
			stats.InsertRange(tile.AsSpan().Slice(i * actualWidth, validWidth));
			rows.Add(stats);
		};
		System.Threading.Tasks.Parallel.For(0, validHeight, body);
		return Stats.Combine(rows.ToArray());
	}

	private Stats RoundTileWithStats(float[,] tile, int TileNumber)
	{
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		_ = Metadata.TileWidth;
		_ = Metadata.TileHeight;
		ConcurrentBag<Stats> rows = new ConcurrentBag<Stats>();
		float noData = Metadata.NoDataValue;
		Action<int> body = (int i) =>
		{
			Stats stats = new Stats(Metadata.NoDataValue);
			for (int j = 0; j < validWidth; j++)
			{
				if (ROUND != null && tile[i, j] != noData)
				{
					tile[i, j] = ROUND(tile[i, j]);
				}
				stats.Insert(tile[i, j]);
			}
			rows.Add(stats);
		};
		System.Threading.Tasks.Parallel.For(0, validHeight, body);
		return Stats.Combine(rows.ToArray());
	}

	public bool RasterHasData()
	{
		if (!TrackStats)
		{
			throw new NotImplementedException();
		}
		if (Stats.Combine(TileStats.Values.ToArray()).CountValid == 0L)
		{
			return false;
		}
		return true;
	}

	protected override void Dispose(bool isDisposing)
	{
		if (!_isDisposed)
		{
			if (Metadata.NoDataValue == float.MinValue)
			{
				Metadata.NoDataValueStringOverride = "-3.40282347E+38";
			}
			if (TrackStats)
			{
				Stats stats = Stats.Combine(TileStats.Values.ToArray());
				SetGDALTagMin(stats.Min.ToString());
				SetGDALTagMax(stats.Max.ToString());
				SetGDALTagMean(stats.Mean.ToString());
				SetGDALTagStdDev(stats.StdDev.ToString());
				SetGDALTagFracNodata(stats.FractionNoData.ToString());
				SetGDALTagDataType(Metadata.DataType);
				SetGDALTagDataUnits(Metadata.DataUnits);
				SetGDALTagRounding(Metadata.Rounding.ToString());
			}
			string gdalTag = GdalTag.ToString();
			if (!string.IsNullOrEmpty(gdalTag))
			{
				_tiffImage.SetField((TiffTag)42112, gdalTag);
			}
			base.Dispose(isDisposing);
		}
	}
}
