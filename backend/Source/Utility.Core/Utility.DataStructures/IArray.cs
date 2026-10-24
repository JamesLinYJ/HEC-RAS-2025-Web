using System.Collections;
using System.Collections.Generic;

namespace Utility.DataStructures;

public interface IArray<T> : IEnumerable<T>, IEnumerable
{
	int Count { get; }

	T this[int index] { get; set; }

	T First();

	T Last();

	List<T> ToList();
}
