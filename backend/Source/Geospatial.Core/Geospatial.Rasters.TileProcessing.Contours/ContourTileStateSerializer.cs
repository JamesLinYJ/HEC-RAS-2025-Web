using System.Collections.Generic;
using System.IO;
using Geospatial.Vectors;

namespace Geospatial.Rasters.TileProcessing.Contours;

public sealed class ContourTileStateSerializer : ITileStateSerializer<ContourTileState>
{
	public void Write(Stream stream, ContourTileState state)
	{
		BinaryWriter binaryWriter = new BinaryWriter(stream);
		binaryWriter.Write(state.Fragments.Count);
		foreach (List<Point> fragment in state.Fragments)
		{
			binaryWriter.Write(fragment.Count);
			foreach (Point fragment2 in fragment)
			{
				binaryWriter.Write(fragment2.X);
				binaryWriter.Write(fragment2.Y);
			}
		}
		binaryWriter.Flush();
	}

	public ContourTileState Read(Stream stream)
	{
		BinaryReader binaryReader = new BinaryReader(stream);
		ContourTileState contourTileState = new ContourTileState();
		int readInt32 = binaryReader.ReadInt32();
		for (int i = 0; i < readInt32; i++)
		{
			int readInt322 = binaryReader.ReadInt32();
			List<Point> points = new List<Point>(readInt322);
			for (int j = 0; j < readInt322; j++)
			{
				points.Add(new Point(binaryReader.ReadDouble(), binaryReader.ReadDouble()));
			}
			contourTileState.Fragments.Add(points);
		}
		return contourTileState;
	}
}
