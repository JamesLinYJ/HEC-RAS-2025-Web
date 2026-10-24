using System;

namespace TiffBinaryReader.TagTypeEnums;

[Flags]
public enum SubfileTypes : ushort
{
	None = 0,
	ReducedImage = 1,
	Page = 2,
	Mask = 4
}
