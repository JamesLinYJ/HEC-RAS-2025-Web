using System.Collections;
using System.Collections.Generic;

namespace Geospatial.Vectors;

public interface IVectorCollection<V> : IReadOnlyVectorCollection<V>, IEnumerable<V>, IEnumerable, ISpatialVersion, IExtent, IReadOnlyList<V>, IReadOnlyCollection<V>
{
	bool IsReadOnly { get; }

	V IReadOnlyList<V>.this[int index] => this[index];

	new V this[int index] { get; set; }

	void Add(V localValue);

	void Insert(int index, V localValue);

	void RemoveAt(int index);

	void Clear();
}
