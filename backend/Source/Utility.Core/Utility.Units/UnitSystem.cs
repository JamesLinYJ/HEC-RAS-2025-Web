using System.Text.Json.Serialization;

namespace Utility.Units;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UnitSystem
{
	[JsonStringEnumMemberName("US Customary")]
	USCustomary,
	[JsonStringEnumMemberName("SI")]
	SI,
	[JsonStringEnumMemberName("null")]
	NullUnitSystem
}
