namespace Utility.DataStructures;

public static class CompactListExtensions
{
	public static CompactList<T> WrapInCompactList<T>(this T localValue)
	{
		return new CompactList<T>(localValue);
	}
}
