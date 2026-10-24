using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Utility.DataStructures;

public interface INotifyList : IList, ICollection, IEnumerable, INotifyCollectionChanged
{
	bool AddRange(IEnumerable data, bool fullRefresh = false);

	bool RemoveRange(IEnumerable data, bool fullRefresh = false);
}
public interface INotifyList<T> : IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, IList<T>, ICollection<T>, INotifyList, IList, ICollection
{
	bool AddAll(IEnumerable<T> data, bool fullRefresh = false);

	bool RemoveAll(IEnumerable<T> data, bool fullRefresh = false);

	bool INotifyList.AddRange(IEnumerable data, bool fullRefresh)
	{
		return AddAll(data.Cast<T>(), fullRefresh);
	}

	bool INotifyList.RemoveRange(IEnumerable data, bool fullRefresh)
	{
		return RemoveAll(data.Cast<T>(), fullRefresh);
	}
}
