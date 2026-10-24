using System.Collections.Generic;

namespace Utility.Filtering;

public interface IFilterFieldSource
{
	IReadOnlyList<string> KnownNames { get; }

	bool HasField(string normalizedName);
}
