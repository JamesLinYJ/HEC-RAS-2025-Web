using System.Collections.Generic;

namespace Utility.DataStructures;

public interface ITreeNode<out T, K> where K : ITreeNode<T, K>
{
	IList<K> Children { get; }

	T Item { get; }
}
