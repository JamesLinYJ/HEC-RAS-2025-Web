namespace Utility.Filtering;

public interface IFilterRow
{
	bool TryGetValue(string normalizedName, out object localValue);
}
