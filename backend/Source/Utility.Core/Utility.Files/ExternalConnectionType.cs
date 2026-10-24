using Utility.Attributes;

namespace Utility.Files;

public enum ExternalConnectionType
{
	[EnumDisplayName("File System")]
	FileSystem,
	[EnumDisplayName("Environment Variable")]
	EnvironmentVariable,
	[EnumDisplayName("S3 Server")]
	S3
}
