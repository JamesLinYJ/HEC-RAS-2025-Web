namespace Utility.DataStructures;

public interface IMutableTreeNodeChildV2<T> : ITreeNodeChildV2<T> where T : ITreeNodeV2<T>
{
	new ITreeNodeV2<T> Parent { get; set; }

	ITreeNodeV2<T> ITreeNodeChildV2<T>.Parent => Parent;
}
