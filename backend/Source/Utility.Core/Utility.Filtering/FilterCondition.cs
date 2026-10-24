using Utility.Attributes;

namespace Utility.Filtering;

public enum FilterCondition
{
	[EnumDisplayName("equals")]
	Equals,
	[EnumDisplayName("does not equal")]
	NotEquals,
	[EnumDisplayName("contains")]
	Contains,
	[EnumDisplayName("does not contain")]
	DoesNotContain,
	[EnumDisplayName("greater than")]
	GreaterThan,
	[EnumDisplayName("at least")]
	AtLeast,
	[EnumDisplayName("less than")]
	LessThan,
	[EnumDisplayName("at most")]
	AtMost,
	[EnumDisplayName("is blank")]
	IsBlank,
	[EnumDisplayName("is not blank")]
	IsNotBlank
}
