using Utility.Attributes;

namespace Ras.Layers;

public enum NValueSource
{
	[Alias("Constant")]
	[EnumDisplayName("Constant")]
	Constant,
	[Alias("Classification Layer")]
	[EnumDisplayName("Classification Layer")]
	ClassificationLayer,
	[Alias("Imported Raster")]
	[EnumDisplayName("Imported Raster")]
	ImportedRaster,
	Debug
}
