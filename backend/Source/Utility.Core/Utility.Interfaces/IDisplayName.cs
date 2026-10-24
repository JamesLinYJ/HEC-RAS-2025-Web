namespace Utility.Interfaces;

public interface IDisplayName : IReadOnlyDisplayName
{
	string IReadOnlyDisplayName.DisplayName => DisplayName;

	new string DisplayName { get; set; }
}
