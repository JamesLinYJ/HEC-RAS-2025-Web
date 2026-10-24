namespace Utility.DataStructures;

public interface ITreeNodeChildV2<T> where T : ITreeNodeV2<T>
{
	ITreeNodeV2<T> Parent { get; }
}
