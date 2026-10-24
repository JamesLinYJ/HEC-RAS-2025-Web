namespace TiffBinaryReader.TagTypeEnums;

public enum PhotometricInterpretations : ushort
{
	WhiteIsZero = 0,
	BlackIsZero = 1,
	RGB = 2,
	RGBPalette = 3,
	TransparencyMask = 4,
	CMYK = 5,
	YCbCr = 6,
	CIELab = 8
}
