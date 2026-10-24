namespace RasMapperLib;

public class DataColumnGroup
{
	public string DisplayName { get; }

	public bool IsVisible { get; set; }

	public DataColumnGroup(string displayName, bool isVisible)
	{
		DisplayName = displayName;
		IsVisible = isVisible;
	}

	public DataColumnGroup GetCopy()
	{
		return new DataColumnGroup(DisplayName, IsVisible);
	}
}
