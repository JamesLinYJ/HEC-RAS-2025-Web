using System;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib.Utilities;

public abstract class Path
{
	public static void EnsureDirectoryExists(string dirname)
	{
		if (!Directory.Exists(dirname))
		{
			Directory.CreateDirectory(dirname);
		}
	}

	public static string CreateWorldFilename(string filename)
	{
		string extension = System.IO.Path.GetExtension(filename);
		string extension2 = Strings.Mid(extension, 2, 1) + Strings.Right(extension, 1) + "w";
		return System.IO.Path.ChangeExtension(filename, extension2);
	}

	public static string FolderName(string filename)
	{
		string[] parts = System.IO.Path.GetDirectoryName(filename).Split(new char[1] { System.IO.Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
		if (parts != null && parts.Count() != 0)
		{
			return parts.Last();
		}
		return "";
	}

	public static string RenameFile(string currentFilename, string newfilename)
	{
		string result;
		if (File.Exists(newfilename))
		{
			try
			{
				File.Delete(newfilename);
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = "Error renaming file: unable to delete the existing file: " + newfilename;
				ProjectData.ClearProjectError();
				goto IL_005a;
			}
		}
		try
		{
			File.Move(currentFilename, newfilename);
		}
		catch (Exception ex3)
		{
			ProjectData.SetProjectError(ex3);
			Exception ex4 = ex3;
			result = "Error renaming file: " + currentFilename + " to: " + newfilename;
			ProjectData.ClearProjectError();
			goto IL_005a;
		}
		result = "";
		goto IL_005a;
		IL_005a:
		return result;
	}

	public static bool CreateOrUpdate(string olderFile, string newerFile)
	{
		if (!File.Exists(newerFile))
		{
			return true;
		}
		if (!File.Exists(olderFile))
		{
			return true;
		}
		if (DateTime.Compare(File.GetLastWriteTime(olderFile), File.GetLastWriteTime(newerFile)) > 0)
		{
			return true;
		}
		return false;
	}

	public static string EnsureFileCharacters(string filename)
	{
		char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
		foreach (char c in invalidFileNameChars)
		{
			filename = filename.Replace(Conversions.ToString(c), "_");
		}
		return filename;
	}

	public static string MakeRelative(string filename, string baseFilename)
	{
		string path = "";
		if (string.IsNullOrEmpty(filename))
		{
			return "";
		}
		string directoryName = System.IO.Path.GetDirectoryName(baseFilename);
		string directoryName2 = System.IO.Path.GetDirectoryName(filename);
		checked
		{
			if (Operators.CompareString(directoryName, directoryName2, TextCompare: false) == 0)
			{
				path = System.IO.Path.Combine(".", System.IO.Path.GetFileName(filename));
			}
			else
			{
				string[] parts = Strings.Split(directoryName2, Conversions.ToString(System.IO.Path.DirectorySeparatorChar));
				string[] partsArray = Strings.Split(directoryName, Conversions.ToString(System.IO.Path.DirectorySeparatorChar));
				int ordinal = -1;
				int count = partsArray.Count() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (i <= parts.Count() - 1)
					{
						if (Operators.CompareString(partsArray[i].ToUpper(), parts[i].ToUpper(), TextCompare: false) != 0)
						{
							break;
						}
						ordinal = i;
					}
				}
				if (ordinal < 0)
				{
					path = filename;
				}
				else
				{
					if (ordinal == partsArray.Count() - 1)
					{
						path = ".";
					}
					else
					{
						int ordinal2 = ordinal + 1;
						int count2 = partsArray.Count() - 1;
						for (int j = ordinal2; j <= count2; j++)
						{
							path = System.IO.Path.Combine(path, "..");
						}
					}
					int ordinal3 = ordinal + 1;
					int count3 = parts.Count() - 1;
					for (int k = ordinal3; k <= count3; k++)
					{
						path = System.IO.Path.Combine(path, parts[k]);
					}
					path = System.IO.Path.Combine(path, System.IO.Path.GetFileName(filename));
				}
			}
			return path;
		}
	}

	public static string MakeAbsolute(string filename, string baseFilename)
	{
		if (Operators.CompareString(filename, "", TextCompare: false) == 0)
		{
			return "";
		}
		string textValue = "";
		string directoryName = System.IO.Path.GetDirectoryName(baseFilename);
		string[] parts = Strings.Split(directoryName, Conversions.ToString(System.IO.Path.DirectorySeparatorChar));
		string[] partsArray = Strings.Split(filename, Conversions.ToString(System.IO.Path.DirectorySeparatorChar));
		checked
		{
			if (partsArray.Length == 1)
			{
				textValue = directoryName + Conversions.ToString(System.IO.Path.DirectorySeparatorChar) + filename;
			}
			else if (Operators.CompareString(partsArray[0], ".", TextCompare: false) == 0)
			{
				textValue = directoryName;
				int count = partsArray.Count() - 1;
				for (int i = 1; i <= count; i++)
				{
					textValue = System.IO.Path.Combine(textValue, partsArray[i]);
				}
			}
			else if (Operators.CompareString(partsArray[0], "..", TextCompare: false) == 0)
			{
				int ordinal = -1;
				int partsArrayTerm = partsArray.Count() - 1;
				for (int j = 0; j <= partsArrayTerm && Operators.CompareString(partsArray[j], "..", TextCompare: false) == 0; j++)
				{
					ordinal = j;
				}
				textValue = System.IO.Path.GetPathRoot(directoryName);
				ordinal++;
				int count2 = parts.Count() - 1 - ordinal;
				for (int k = 1; k <= count2; k++)
				{
					textValue = System.IO.Path.Combine(textValue, parts[k]);
				}
				int ordinal2 = ordinal;
				int count3 = partsArray.Count() - 1;
				for (int l = ordinal2; l <= count3; l++)
				{
					textValue = System.IO.Path.Combine(textValue, partsArray[l]);
				}
			}
			else
			{
				textValue = filename;
			}
			return textValue;
		}
	}
}
