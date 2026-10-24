using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Rasters;
using Utility.Extensions;
using Utility.Progress;

namespace Ras.Mapping;

public static class CalculatedMapEngine
{
	public static void ComputeAcrossTimeAndSpace(ITileWriter<float> output, IStaticMapComputer staticMapComputer, ITileReaderWriter<float> CacheReaderWriter, CalculatedMapTuningParams options, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		BlockSize bs = ((IBlockedRasterDefinition)CacheReaderWriter).BlockSize;
		int tileRasterCellCount = CacheReaderWriter.RasterDefinition.GetBlockRaster(bs, 0).CellCount;
		int timestepsPerChunk = options.TimestepsPerChunk;
		int profileCount = staticMapComputer.ProfileCount;
		int count = profileCount / timestepsPerChunk;
		int timestepsPerChunk2 = profileCount % timestepsPerChunk;
		new ThreadLocal<float[]>(() => new float[tileRasterCellCount]);
		ThreadLocal<float[]> tileValueBuffer = new ThreadLocal<float[]>(() => new float[tileRasterCellCount]);
		for (int timeChunkIdx = 0; timeChunkIdx <= count; timeChunkIdx++)
		{
			pr.ReportProgressFraction((float)timeChunkIdx / (float)(count + 1));
			int startingProfileIndex = timeChunkIdx * timestepsPerChunk;
			if (timeChunkIdx == count)
			{
				timestepsPerChunk = timestepsPerChunk2;
			}
			Parallel.For(0, CacheReaderWriter.TileCount, (int tileIdx) =>
			{
				RasterDefinition blockRaster = CacheReaderWriter.RasterDefinition.GetBlockRaster(bs, tileIdx);
				if (timeChunkIdx > 0)
				{
					CacheReaderWriter.ReadBlock(tileIdx, MemoryMarshal.AsMemory<float>(tileValueBuffer.Value));
				}
				else
				{
					tileValueBuffer.Value.Fill(-9999f);
				}
				StaticCache staticCache = staticMapComputer.GetStaticCache(blockRaster);
				for (int i = 0; i < timestepsPerChunk; i++)
				{
					staticMapComputer.AccumulateStaticMap(startingProfileIndex + i, staticCache, tileValueBuffer.Value);
				}
				CacheReaderWriter.WriteBlock(tileIdx, tileValueBuffer.Value);
			});
		}
		pr.ReportProgressFraction(0.99f);
		for (int ordinal = 0; ordinal < CacheReaderWriter.TileCount; ordinal++)
		{
			float[] tile = CacheReaderWriter.ReadTile(ordinal);
			output.WriteTile(ordinal, tile);
		}
		pr.ReportProgressFraction(1f);
	}
}
