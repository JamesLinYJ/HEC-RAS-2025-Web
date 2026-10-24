namespace Utility.DataStructures;

public interface IConstructableTree<T, D>
{
	IReadOnlyAdvancedCollection<T> Items { get; }

	void AddItem(T localValue);

	D ConstructTree();
}
