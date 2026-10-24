namespace Utility.DataStructures;

public interface ITreeNodeChild<T, K> where K : ITreeNode<T, K>
{
	ITreeNode<T, K> Parent { get; }
}
