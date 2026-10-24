using Utility.Attributes;

namespace Ras.Engine;

public enum TVDGradientRatioMethod
{
	[EnumDisplayName("Darwish-Moukalled Cell Gradient")]
	DarwishMoukalledCellGradient,
	[EnumDisplayName("Upstream Virtual Point")]
	UpstreamVirtualPoint,
	[EnumDisplayName("Casulli Face-Flow Weighted")]
	CasulliFaceFlowWeighted
}
