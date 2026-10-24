using System.Runtime.Serialization;

namespace Geospatial.Vectors.Topology;

public enum SizeFunction
{
	[EnumMember(Value = "Constant")]
	Constant,
	[EnumMember(Value = "TIN")]
	Tin
}
