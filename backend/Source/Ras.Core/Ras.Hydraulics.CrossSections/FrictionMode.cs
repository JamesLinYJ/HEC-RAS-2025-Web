using Utility.Attributes;

namespace Ras.Hydraulics.CrossSections;

public enum FrictionMode
{
	[Alias("Basic Mann n")]
	BasicManningsN,
	[Alias("Stationing")]
	StationMannings,
	RoughnessHeightK,
	VerticalVariation,
	FlowVariationN
}
