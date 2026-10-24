using System.Collections.Generic;

namespace Utility.Filtering;

public sealed class PathCall
{
	public IReadOnlyList<FilterLiteral> Arguments { get; }

	public string RawText { get; }

	public PathCall(IReadOnlyList<FilterLiteral> arguments, string rawText)
	{
		Arguments = arguments;
		RawText = rawText;
	}
}
