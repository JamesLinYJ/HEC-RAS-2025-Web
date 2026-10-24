using System.Collections.Generic;

namespace Utility.DataStructures;

public interface IReadOnlyMap<T1, T2>
{
	int Count { get; }

	IReadOnlyDictionary<T1, T2> Forward { get; }

	IReadOnlyDictionary<T2, T1> Backward { get; }

	bool ContainsKey(T1 key);

	bool ContainsValue(T2 t2);

	bool TryGetValue(T1 key, out T2 t2);

	bool TryGetKey(T2 t2, out T1 key);

	Map<T1, T2> ToMap();
}
