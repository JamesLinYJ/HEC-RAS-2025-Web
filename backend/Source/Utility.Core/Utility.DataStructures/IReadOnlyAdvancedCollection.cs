using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Utility.DataStructures;

public interface IReadOnlyAdvancedCollection<T> : IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged
{
	bool Contains(T element);

	int IndexOf(T element);
}
