using Utility.Attributes;

namespace Ras.Mapping;

public enum MapTypes
{
	Depth,
	[EnumDisplayName("Water Surface")]
	WaterSurfaceElevation,
	Velocity,
	Flow,
	ResidenceTime,
	Courant,
	Froude,
	EnergyDepth,
	EnergyElevation,
	DepthTimesVelocity,
	DepthTimesVelocitySquared,
	ShearStress,
	StreamPower,
	ArrivalTime,
	ArrivalTimeMax,
	Duration,
	Recession,
	FractionInundated,
	CellWet,
	DepthRange,
	VelocityRange,
	ArrivalRange,
	LaplacianU,
	LaplacianV,
	Generic,
	Unknown
}
