using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using BitMiracle.LibTiff.Classic;
using Geospatial.Rasters;
using Utility;

namespace TiffAssist;

public class DoubleTiffWriter : TiffWriter<double>
{
	private readonly Func<double, double> ROUND;

	public DoubleTiffWriter(string filename, int imageWidth, int imageHeight, TiffMetadata<double> metadata, bool deleteExistingFile = true)
		: base(filename, imageWidth, imageHeight, SampleFormat.IEEEFP, false, metadata, "", deleteExistingFile)
	{
		if (double.IsNaN(metadata.NoDataValue))
		{
			throw new ArgumentException("Cannot have input value NaN for NoData.");
		}
		switch (Metadata.Rounding)
		{
		case RoundTo.None:
			ROUND = null;
			break;
		case RoundTo.Tenths:
			ROUND = (double f) => Math.Round(f, 1);
			break;
		case RoundTo.Hundredths:
			ROUND = (double f) => Math.Round(f, 2);
			break;
		case RoundTo.Thousandths:
			ROUND = (double f) => Math.Round(f, 3);
			break;
		case RoundTo.Sixteenths:
			ROUND = (double f) => Math.Round(f * 16.0) / 16.0;
			break;
		case RoundTo.ThirtySeconds:
			ROUND = (double f) => Math.Round(f * 32.0) / 32.0;
			break;
		case RoundTo.SixtyFourths:
			ROUND = (double f) => Math.Round(f * 64.0) / 64.0;
			break;
		case RoundTo.HundredTwentyEighths:
			ROUND = (double f) => Math.Round(f * 128.0) / 128.0;
			break;
		case RoundTo.ThousandTwentyFourths:
			ROUND = (double f) => Math.Round(f * 1024.0) / 1024.0;
			break;
		default:
			throw new Exception("Round Type not supported.");
		}
	}

	public override void WriteTile(int TileNumber, double[] data)
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

	public override void WriteTile(int TileNumber, double[,] data)
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

	private void RoundTileNoStats(double[] tile, int TileNumber)
	{
		if (ROUND == null)
		{
			return;
		}
		double noData = Metadata.NoDataValue;
		int actualWidth = Metadata.TileWidth;
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		System.Threading.Tasks.Parallel.For(0, validHeight, (int i) =>
		{
			for (int j = 0; j < validWidth; j++)
			{
				int actualWidthTerm = i * actualWidth + j;
				if (tile[actualWidthTerm] != noData)
				{
					tile[actualWidthTerm] = ROUND(tile[actualWidthTerm]);
				}
			}
		});
	}

	private void RoundTileNoStats(double[,] tile, int TileNumber)
	{
		if (ROUND == null)
		{
			return;
		}
		double noData = Metadata.NoDataValue;
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		System.Threading.Tasks.Parallel.For(0, validHeight, (int i) =>
		{
			for (int j = 0; j < validWidth; j++)
			{
				if (tile[i, j] != noData)
				{
					tile[i, j] = ROUND(tile[i, j]);
				}
			}
		});
	}

	private Stats RoundTileWithStats(double[] tile, int TileNumber)
	{
		double noData = Metadata.NoDataValue;
		int actualWidth = Metadata.TileWidth;
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		ConcurrentBag<Stats> rows = new ConcurrentBag<Stats>();
		System.Threading.Tasks.Parallel.For(0, validHeight, (int i) =>
		{
			Stats stats = new Stats(noData);
			for (int j = 0; j < validWidth; j++)
			{
				int actualWidthTerm = i * actualWidth + j;
				if (ROUND != null && tile[actualWidthTerm] != noData)
				{
					tile[actualWidthTerm] = ROUND(tile[actualWidthTerm]);
				}
			}
			stats.InsertRange(tile.AsSpan().Slice(i * actualWidth, validWidth));
			rows.Add(stats);
		});
		return Stats.Combine(rows.ToArray());
	}

	private Stats RoundTileWithStats(double[,] tile, int TileNumber)
	{
		double noData = Metadata.NoDataValue;
		GetValidRegion(CurrentDir, TileNumber, out var validWidth, out var validHeight);
		ConcurrentBag<Stats> rows = new ConcurrentBag<Stats>();
		System.Threading.Tasks.Parallel.For(0, validHeight, (int i) =>
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
		});
		return Stats.Combine(rows.ToArray());
	}
}
