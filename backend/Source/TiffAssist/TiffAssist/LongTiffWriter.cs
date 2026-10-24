using System;
using BitMiracle.LibTiff.Classic;

namespace TiffAssist;

public class LongTiffWriter : TiffWriter<long>
{
	public LongTiffWriter(string filename, int imageWidth, int imageHeight, TiffMetadata<long> metadata, bool deleteExistingFile = true)
		: base(filename, imageWidth, imageHeight, SampleFormat.INT, false, metadata, "", deleteExistingFile)
	{
	}

	public override void WriteTile(int tileNumber, long[] data)
	{
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(tileNumber, byteValues);
	}

	public override void WriteTile(int tileNumber, long[,] data)
	{
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(tileNumber, byteValues);
	}
}
