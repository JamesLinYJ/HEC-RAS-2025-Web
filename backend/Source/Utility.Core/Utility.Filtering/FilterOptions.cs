using System;

namespace Utility.Filtering;

public class FilterOptions
{
	public static readonly FilterOptions Default = new FilterOptions();

	public bool CaseSensitiveStrings { get; set; }

	public StringComparison StringComparison
	{
		get
		{
			if (!CaseSensitiveStrings)
			{
				return StringComparison.OrdinalIgnoreCase;
			}
			return StringComparison.Ordinal;
		}
	}
}
