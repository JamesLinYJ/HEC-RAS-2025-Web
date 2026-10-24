namespace Utility.DataStructures;

public static class AdvancedCollectionExtensions
{
	public static ReadOnlyAdvancedCollection<T> AsReadOnly<T>(this AdvancedCollection<T> collection)
	{
		return new ReadOnlyAdvancedCollection<T>(collection);
	}
}
