namespace Utility.Filtering;

public interface ISelectorElement : IFilterRow
{
	bool TrySetValue(string normalizedName, object localValue, out string error);
}
