namespace TiffBinaryReader.TagTypeEnums;

public enum CompressionTypes : ushort
{
	Uncompressed = 1,
	CCITT_1D = 2,
	Group_3_Fax = 3,
	Group_4_Fax = 4,
	LZW = 5,
	JPEG_Old = 6,
	JPEG_New = 7,
	Deflate = 8,
	PackBits = 32773
}
