using Utility.Attributes;

namespace Ras.Layers.Meteorology;

public enum DataLocation
{
	[Alias("Local")]
	Local,
	[Alias("Remote")]
	Remote
}
