using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Utility.DataStructures;

public interface IAdvancedCollection<T> : IReadOnlyAdvancedCollection<T>, IReadOnlyNotifyCollection<T>, IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged, ICollection<T>, ICollection, IList<T>, IList, INotifyPropertyChanged, INotifyList<T>, INotifyList
{
	void SetRange(IList<int> indices, IEnumerable<T> dataList, bool fullRefresh = false);

	void Set(IEnumerable<T> data, bool fullRefresh = false);
}
