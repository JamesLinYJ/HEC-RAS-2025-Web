using System;
using System.IO;

namespace Utility;

public static class DirectoryHelpers
{
	public static bool TryEnsureDir(string folder, out DirectoryEnsureStatus reason)
	{
		try
		{
			EnsureDir(folder);
			reason = DirectoryEnsureStatus.Success;
			return true;
		}
		catch (PathTooLongException)
		{
			reason = DirectoryEnsureStatus.PathTooLong;
		}
		catch (DirectoryNotFoundException)
		{
			reason = DirectoryEnsureStatus.DirectoryNotFound;
		}
		catch (IOException)
		{
			reason = DirectoryEnsureStatus.PathIsFile;
		}
		catch (UnauthorizedAccessException)
		{
			reason = DirectoryEnsureStatus.Unauthorized;
		}
		catch (ArgumentException)
		{
			reason = DirectoryEnsureStatus.InvalidPath;
		}
		catch (NotSupportedException)
		{
			reason = DirectoryEnsureStatus.InvalidPath;
		}
		catch
		{
			reason = DirectoryEnsureStatus.Unknown;
		}
		return false;
	}

	public static string EnsureDir(string folder)
	{
		if (!Directory.Exists(folder))
		{
			Directory.CreateDirectory(folder);
		}
		return folder;
	}

	public static bool CanWrite(string folder)
	{
		if (!Directory.Exists(folder))
		{
			throw new DirectoryNotFoundException(folder);
		}
		string path = Guid.NewGuid().ToString();
		string path2 = Path.Combine(folder, path);
		try
		{
			File.WriteAllText(path2, "test");
			return true;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (File.Exists(path2))
			{
				File.Delete(path2);
			}
		}
	}

	public static string GetDescription(this DirectoryEnsureStatus reason)
	{
		return reason switch
		{
			DirectoryEnsureStatus.PathIsFile => "The directory is the path of a file.", 
			DirectoryEnsureStatus.Unauthorized => "Invalid permissions for the directory.", 
			DirectoryEnsureStatus.InvalidPath => "The directory path is invalid.", 
			DirectoryEnsureStatus.PathTooLong => "The directory path is too long.", 
			DirectoryEnsureStatus.DirectoryNotFound => "The directory was not found, potentially a disconnected USB or network drive.", 
			_ => "Unknown error type.", 
		};
	}
}
