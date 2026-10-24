using System;
using BitMiracle.LibTiff.Classic;
using Geospatial.Rasters;

namespace TiffAssist;

public class ByteTiffWriter : TiffWriter<byte>
{
	public ByteTiffWriter(string filename, int imageWidth, int imageHeight, TiffMetadata<byte> metadata, bool deleteExistingFile = true)
		: base(filename, imageWidth, imageHeight, SampleFormat.UINT, false, metadata, "", deleteExistingFile)
	{
		if (metadata.Rounding != RoundTo.None)
		{
			throw new ArgumentException("No rounding is supported for ByteTiffReader.");
		}
	}

	public override void WriteTile(int tileNumber, byte[] data)
	{
		WriteTileInternal(tileNumber, data);
	}

	public override void WriteTile(int tileNumber, byte[,] data)
	{
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(tileNumber, byteValues);
	}
}
