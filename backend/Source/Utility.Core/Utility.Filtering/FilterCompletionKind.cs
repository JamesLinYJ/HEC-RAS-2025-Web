namespace Utility.Filtering;

public enum FilterCompletionKind
{
	None,
	Field,
	Operator,
	Value,
	Conjunction,
	ListOpen,
	NullCheck
}
