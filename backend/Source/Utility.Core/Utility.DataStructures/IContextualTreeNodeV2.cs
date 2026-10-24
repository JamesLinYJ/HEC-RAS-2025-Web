namespace Utility.DataStructures;

public interface IContextualTreeNodeV2<T> : ITreeNodeV2<T> where T : ITreeNodeV2<T>
{
	object Context { get; }
}
