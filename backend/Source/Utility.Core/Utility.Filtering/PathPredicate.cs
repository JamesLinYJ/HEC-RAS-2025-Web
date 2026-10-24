namespace Utility.Filtering;

public sealed class PathPredicate
{
	public bool IsIndex { get; }

	public int Index { get; }

	public FilterExpression Filter { get; }

	public string RawText { get; }

	private PathPredicate(bool isIndex, int index, FilterExpression filter, string rawText)
	{
		IsIndex = isIndex;
		Index = index;
		Filter = filter;
		RawText = rawText;
	}

	public static PathPredicate FromIndex(int index, string rawText)
	{
		return new PathPredicate(isIndex: true, index, null, rawText);
	}

	public static PathPredicate FromFilter(FilterExpression filter, string rawText)
	{
		return new PathPredicate(isIndex: false, 0, filter, rawText);
	}
}
