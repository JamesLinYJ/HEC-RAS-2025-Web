using System;
using BitMiracle.LibTiff.Classic;

namespace TiffAssist;

public class IntTiffWriter : TiffWriter<int>
{
	public IntTiffWriter(string filename, int imageWidth, int imageHeight, TiffMetadata<int> metadata, bool deleteExistingFile = true)
		: base(filename, imageWidth, imageHeight, SampleFormat.INT, false, metadata, "", deleteExistingFile)
	{
	}

	public override void WriteTile(int tileNumber, int[] data)
	{
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(tileNumber, byteValues);
	}

	public override void WriteTile(int tileNumber, int[,] data)
	{
		byte[] byteValues = new byte[data.Length * SizeOfT];
		Buffer.BlockCopy(data, 0, byteValues, 0, byteValues.Length);
		WriteTileInternal(tileNumber, byteValues);
	}
}
