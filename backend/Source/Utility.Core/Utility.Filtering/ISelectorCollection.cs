using System;
using System.Collections.Generic;

namespace Utility.Filtering;

public interface ISelectorCollection
{
	string Name { get; }

	int Count { get; }

	IReadOnlyList<string> ProjectionFields { get; }

	IReadOnlyList<string> KnownFieldNames { get; }

	ISelectorElement GetElement(int index);

	bool TryGetField(string normalizedName, out Type type, out bool settable);
}
