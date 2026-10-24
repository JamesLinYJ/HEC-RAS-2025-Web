namespace Utility.Files;

public enum FileState
{
	Exists,
	FileNotFound,
	DriveLetterNotFound,
	PermissionError,
	PathTooLong,
	UnknownError
}
