using System;
using System.Collections.Generic;

namespace Utility.Filtering;

public interface ISelectorFields
{
	IReadOnlyList<string> FieldNames { get; }

	bool TryGetField(string normalizedName, out Type type, out bool settable);

	bool TryGetValue(string normalizedName, out object localValue);

	bool TrySetValue(string normalizedName, object localValue, out string error);
}
