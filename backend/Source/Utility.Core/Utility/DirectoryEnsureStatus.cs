namespace Utility;

public enum DirectoryEnsureStatus
{
	Success,
	PathIsFile,
	Unauthorized,
	InvalidPath,
	PathTooLong,
	DirectoryNotFound,
	Unknown
}
