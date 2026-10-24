using System;

namespace Utility.Logging;

public static class FileMessageCodes
{
	public static readonly MessageClass FileNotFound = new MessageClass("IO_FileNotFound", MessageSeverity.Error, "File \"{Filename}\" does not exist.");

	public static readonly MessageClass FileAlreadyExists = new MessageClass("IO_FileAlreadyExists", MessageSeverity.Error, "File \"{Filename}\" already exists.");

	public static readonly MessageClass FileIncorrectExtension = new MessageClass("IO_FileIncorrectExtension", MessageSeverity.Error, "\"{Filename}\" does not have the expected file extension \"{ExpectedExtension}\".");

	public static readonly MessageClass FileCouldNotBeDeleted = new MessageClass("IO_FileCouldNotBeDeleted", MessageSeverity.Error, "File \"{Filename}\" could not be deleted.");

	public static readonly MessageClass DirectoryNotFound = new MessageClass("IO_DirectoryNotFound", MessageSeverity.Error, "Directory \"{Directory}\" does not exist.");

	public static readonly MessageClass FileOrDirectoryNotFound = new MessageClass("IO_FileOrDirectoryNotFound", MessageSeverity.Error, "\"{FileOrDirectory}\" is not a valid File or Directory.");

	public static readonly MessageClass DirectoryHasNoFilesWithExpectedExtension = new MessageClass("IO_DirectoryHasNoFilesWithExpectedExtension", MessageSeverity.Error, "Directory \"{Directory}\" has no files with expected extension \"{ExpectedExtension}\".");

	public static readonly MessageClass DirectoryHasMoreThanOneFileWithExpectedExtension = new MessageClass("IO_DirectoryHasMoreThanOneFileWithExpectedExtension", MessageSeverity.Error, "Directory \"{Directory}\" has more than one file with expected extension \"{ExpectedExtension}\".");

	public static readonly MessageClass DriveNotFound = new MessageClass("IO_DriveNotFound", MessageSeverity.Error, "Drive \"{Drive}\" does not exist.");

	public static readonly MessageClass FileTooSmall = new MessageClass("IO_FileTooSmall", MessageSeverity.Error, "File \"{Filename}\" is too short, expected at least {ByteCount} bytes.");

	public static readonly MessageClass FileTooLarge = new MessageClass("IO_FileTooLarge", MessageSeverity.Error, "File \"{Filename}\" is too large, expected at most {ByteCount} bytes.");

	public static readonly MessageClass FileSizeDoesntMatch = new MessageClass("IO_FileSizeDoesntMatch", MessageSeverity.Error, "File \"{Filename}\" doesn't match the expected size. Expected: {ByteCount}, Actual: {ByteCount}.");

	public static readonly MessageClass FileTimestampDoesntMatch = new MessageClass("IO_FileTimestampDoesntMatch", MessageSeverity.Error, "File \"{Filename}\" doesn't match the expected timestamp. Expected: {Timestamp}, Actual: {Timestamp}.");

	public static readonly MessageClass FileHashDoesntMatch = new MessageClass("IO_FileHashDoesntMatch", MessageSeverity.Error, "File \"{Filename}\" doesn't match the expected hash. Expected: \"{Hash}\", Actual: \"{Hash}\".");

	public static Message GetFileNotFound(string filename)
	{
		return FileNotFound.Create(filename);
	}

	public static Message GetFileAlreadyExists(string filename)
	{
		return FileAlreadyExists.Create(filename);
	}

	public static Message GetFileIncorrectExtension(string filename, string expectedExtension)
	{
		return FileIncorrectExtension.Create(filename, expectedExtension);
	}

	public static Message GetFileCouldNotBeDeleted(string filename)
	{
		return FileCouldNotBeDeleted.Create(filename);
	}

	public static Message GetDirectoryNotFound(string dir)
	{
		return DirectoryNotFound.Create(dir);
	}

	public static Message GetFileOrDirectoryNotFound(string source)
	{
		return FileOrDirectoryNotFound.Create(source);
	}

	public static Message GetDirectoryHasNoFilesWithExpectedExtension(string dir, string ext)
	{
		return DirectoryHasNoFilesWithExpectedExtension.Create(dir, ext);
	}

	public static Message GetDirectoryHasMoreThanOneFileWithExpectedExtension(string dir, string ext)
	{
		return DirectoryHasMoreThanOneFileWithExpectedExtension.Create(dir, ext);
	}

	public static Message GetDriveNotFound(string drive)
	{
		return DriveNotFound.Create(drive);
	}

	public static Message GetFileTooSmall(string filename, int expectedMinimumSize)
	{
		return FileTooSmall.Create(filename, expectedMinimumSize);
	}

	public static Message GetFileTooLarge(string filename, int expectedMaximumSize)
	{
		return FileTooLarge.Create(filename, expectedMaximumSize);
	}

	public static Message GetFileSizeDoesntMatch(string filename, long expectedSize, long actualSize)
	{
		return FileSizeDoesntMatch.Create(filename, expectedSize, actualSize);
	}

	public static Message GetFileTimestampDoesntMatch(string filename, DateTime expectedDt, DateTime actualDt)
	{
		return FileTimestampDoesntMatch.Create(filename, expectedDt, actualDt);
	}

	public static Message GetFileHashDoesntMatch(string filename, string expectedHash, string actualHash)
	{
		return FileHashDoesntMatch.Create(filename, expectedHash, actualHash);
	}
}
