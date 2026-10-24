using Utility.Attributes;

namespace Utility.Files;

public enum ExternalConnectionTarget
{
	[EnumDisplayName("File")]
	File,
	[EnumDisplayName("Directory")]
	Directory
}
