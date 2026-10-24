namespace RasMapperLib;

public class ByteBitCompressedData
{
	public int CountOn;

	public int Length;

	public byte[] CompressedBytes;

	public ByteBitCompressedData(int countOn, int length, byte[] compressedBytes)
	{
		CountOn = countOn;
		Length = length;
		CompressedBytes = compressedBytes;
	}
}
