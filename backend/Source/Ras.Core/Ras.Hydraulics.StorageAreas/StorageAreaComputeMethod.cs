using Utility.Attributes;

namespace Ras.Hydraulics.StorageAreas;

public enum StorageAreaComputeMethod
{
	[Alias("Simple Area")]
	AreaTimesDepth,
	[Alias("Elev Vol RC")]
	ElevationVolumeCurve
}
