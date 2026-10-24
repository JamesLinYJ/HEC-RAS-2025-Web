using Utility.Attributes;

namespace Ras.Layers.HydraulicLayers;

public enum WeirTypes
{
	[Alias("Ogee")]
	Ogee,
	[Alias("SharpCrested")]
	SharpCrested,
	[Alias("BroadCrested")]
	BroadCrested
}
