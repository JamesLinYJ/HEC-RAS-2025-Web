using System.ComponentModel;
using Utility.Attributes;

namespace Geospatial.Rasters;

public enum InterpolationMode
{
	[Description("Nearest")]
	[EnumDisplayName("Nearest Neighbor")]
	Nearest,
	[Description("Render-Block")]
	[EnumDisplayName("Render-Block")]
	Linear,
	[Description("Smooth")]
	[EnumDisplayName("Smooth")]
	Smooth,
	[Description("Area Avg")]
	[EnumDisplayName("Area Average")]
	AreaAveraged,
	[Description("Hybrid")]
	[EnumDisplayName("Hybrid")]
	Hybrid
}
