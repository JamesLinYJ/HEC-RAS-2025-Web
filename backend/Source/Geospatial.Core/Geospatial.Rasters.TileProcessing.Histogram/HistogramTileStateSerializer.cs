using System.IO;

namespace Geospatial.Rasters.TileProcessing.Histogram;

public sealed class HistogramTileStateSerializer : ITileStateSerializer<HistogramTileState>
{
	private readonly int _binCount;

	public HistogramTileStateSerializer(int binCount)
	{
		_binCount = binCount;
	}

	public void Write(Stream stream, HistogramTileState state)
	{
		BinaryWriter binaryWriter = new BinaryWriter(stream);
		binaryWriter.Write(state.NoDataCount);
		binaryWriter.Write(state.ObservedMin);
		binaryWriter.Write(state.ObservedMax);
		long[] counts = state.Counts;
		foreach (long count in counts)
		{
			binaryWriter.Write(count);
		}
		binaryWriter.Flush();
	}

	public HistogramTileState Read(Stream stream)
	{
		BinaryReader binaryReader = new BinaryReader(stream);
		HistogramTileState histogramTileState = new HistogramTileState(_binCount)
		{
			NoDataCount = binaryReader.ReadInt64(),
			ObservedMin = binaryReader.ReadSingle(),
			ObservedMax = binaryReader.ReadSingle()
		};
		for (int i = 0; i < _binCount; i++)
		{
			histogramTileState.Counts[i] = binaryReader.ReadInt64();
		}
		return histogramTileState;
	}
}
