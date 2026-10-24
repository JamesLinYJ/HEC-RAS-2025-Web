namespace Utility.Filtering;

internal enum FilterTokenKind
{
	Identifier,
	Number,
	String,
	Equal,
	NotEqual,
	Less,
	LessOrEqual,
	Greater,
	GreaterOrEqual,
	LeftParen,
	RightParen,
	Comma,
	End
}
