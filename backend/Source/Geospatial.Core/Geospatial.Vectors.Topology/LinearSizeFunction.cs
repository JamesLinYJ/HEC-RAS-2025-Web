using System.Runtime.Serialization;
using Utility.Attributes;

namespace Geospatial.Vectors.Topology;

public enum LinearSizeFunction
{
	[EnumMember(Value = "Constant")]
	[EnumDisplayName("Size")]
	Constant = 0,
	[EnumMember(Value = "Count-Bias")]
	[EnumDisplayName("Count")]
	CountWithBias = 1,
	[EnumMember(Value = "Blank")]
	[EnumDisplayName("Blank")]
	Unset = 3
}
