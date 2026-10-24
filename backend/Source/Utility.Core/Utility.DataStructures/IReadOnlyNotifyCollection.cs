using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Utility.DataStructures;

public interface IReadOnlyNotifyCollection<out T> : IReadOnlyList<T>, IEnumerable<T>, IEnumerable, IReadOnlyCollection<T>, INotifyCollectionChanged
{
}
