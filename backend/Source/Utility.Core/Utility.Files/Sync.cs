using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Utility.Progress;

namespace Utility.Files;

public class Sync
{
	public static bool IgnoreFileAttributeChanges = true;

	public virtual DirectoryInfo SourceDirectory { get; set; }

	public virtual DirectoryInfo DestinationDirectory { get; set; }

	public InputParams Configuration { get; set; }

	public static SyncResults Start(string srcDirectory, string destDirectory)
	{
		return new Sync(srcDirectory, destDirectory).Start();
	}

	public Sync()
	{
		Configuration = new InputParams();
	}

	public Sync(string sourceDirectory, string destinationDirectory)
		: this()
	{
		SourceDirectory = new DirectoryInfo(sourceDirectory);
		DestinationDirectory = new DirectoryInfo(destinationDirectory);
	}

	public static void CopyFileWithAttributes(string srcFile, string dstFile)
	{
		File.Copy(srcFile, dstFile, overwrite: true);
		CopyAttributes(srcFile, dstFile);
	}

	public static void CopyAttributes(string srcFile, string dstFile)
	{
		if (!FileHelpers.CanReadFile(srcFile))
		{
			throw new ArgumentException(srcFile + " does not exist");
		}
		if (!FileHelpers.CanReadFile(dstFile))
		{
			throw new ArgumentException(dstFile + " does not exist");
		}
		File.SetAttributes(dstFile, File.GetAttributes(srcFile));
		File.SetLastWriteTime(dstFile, File.GetLastWriteTime(srcFile));
	}

	public static bool MatchesAllAttributes(string file1, string file2)
	{
		if (!FileHelpers.CanReadFile(file1))
		{
			return false;
		}
		if (!FileHelpers.CanReadFile(file2))
		{
			return false;
		}
		DateTime lastWriteTime = File.GetLastWriteTime(file1);
		DateTime lastWriteTime2 = File.GetLastWriteTime(file2);
		if (lastWriteTime != lastWriteTime2)
		{
			return false;
		}
		return true;
	}

	public virtual SyncResults Start(ProgressReporter reporter = null)
	{
		SyncResults results = new SyncResults();
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (Validate(SourceDirectory.FullName, DestinationDirectory.FullName, Configuration))
		{
			ProcessDirectory(SourceDirectory.FullName, DestinationDirectory.FullName, Configuration, ref results, reporter);
		}
		return results;
	}

	public async Task<SyncResults> StartAsync(InputParams configuration, ProgressReporter reporter = null)
	{
		return await Task.Factory.StartNew(() => Start(configuration, reporter));
	}

	public async Task<SyncResults> StartAsync(ProgressReporter reporter = null)
	{
		return await Task.Factory.StartNew(() => Start(reporter));
	}

	public virtual SyncResults Start(InputParams configuration, ProgressReporter reporter = null)
	{
		Configuration = configuration;
		return Start(reporter);
	}

	public virtual void DeleteDirectory(DirectoryInfo directory)
	{
		FileInfo[] files = directory.GetFiles("*.*", SearchOption.AllDirectories);
		foreach (FileInfo fileInfo in files)
		{
			if (fileInfo.IsReadOnly)
			{
				fileInfo.IsReadOnly = false;
			}
		}
		DirectoryInfo[] directories = directory.GetDirectories("*.*", SearchOption.AllDirectories);
		foreach (DirectoryInfo directoryInfo in directories)
		{
			if ((directoryInfo.Attributes & FileAttributes.ReadOnly) > FileAttributes.None)
			{
				directoryInfo.Attributes &= ~FileAttributes.ReadOnly;
			}
		}
		if ((directory.Attributes & FileAttributes.ReadOnly) > FileAttributes.None)
		{
			directory.Attributes &= ~FileAttributes.ReadOnly;
		}
		directory.Delete(recursive: true);
	}

	public virtual FileInfo[] GetFiles(DirectoryInfo directoryInfo, InputParams inputParams, ref SyncResults results)
	{
		List<FileInfo> files = new List<FileInfo>(directoryInfo.GetFiles());
		if (inputParams != null && inputParams.AreSourceFilesFiltered)
		{
			for (int i = 0; i < files.Count; i++)
			{
				FileInfo fileInfo = files[i];
				if ((!inputParams.ExcludeHidden || (fileInfo.Attributes & FileAttributes.Hidden) <= FileAttributes.None) && !ShouldExclude(inputParams.ExcludeFiles, inputParams.IncludeFiles, fileInfo.Name))
				{
					Func<string, bool> shouldExcludeFile = inputParams.ShouldExcludeFile;
					if (shouldExcludeFile == null || !shouldExcludeFile(fileInfo.FullName))
					{
						continue;
					}
				}
				files.RemoveAt(i);
				results.FilesIgnored++;
				i--;
			}
		}
		return files.ToArray();
	}

	public virtual DirectoryInfo[] GetDirectories(DirectoryInfo directoryInfo, InputParams inputParams, ref SyncResults results)
	{
		List<DirectoryInfo> directories = new List<DirectoryInfo>(directoryInfo.GetDirectories());
		if (inputParams != null && inputParams.AreSourceFilesFiltered)
		{
			for (int i = 0; i < directories.Count; i++)
			{
				DirectoryInfo directoryInfo2 = directories[i];
				if ((inputParams.ExcludeHidden && (directoryInfo2.Attributes & FileAttributes.Hidden) > FileAttributes.None) || ShouldExclude(inputParams.ExcludeDirs, inputParams.IncludeDirs, directoryInfo2.Name))
				{
					directories.RemoveAt(i);
					results.DirectoriesIgnored++;
					i--;
				}
			}
		}
		return directories.ToArray();
	}

	private bool Validate(string srcDir, string destDir, InputParams parameters, ProgressReporter reporter = null)
	{
		if (reporter == null)
		{
			reporter = ProgressReporter.None();
		}
		if (parameters == null)
		{
			reporter.ReportMessage("Error: Parameters are null.");
			return false;
		}
		if ((parameters.IncludeFiles != null && parameters.ExcludeFiles != null) || (parameters.IncludeDirs != null && parameters.ExcludeDirs != null))
		{
			reporter.ReportMessage("Error: Can't Include *and* Exclude files simultaneously?.");
			return false;
		}
		string fullPath = Path.GetFullPath(srcDir);
		string fullPath2 = Path.GetFullPath(destDir);
		if (destDir.StartsWith(fullPath) || srcDir.StartsWith(fullPath2))
		{
			string? directoryName = Path.GetDirectoryName(fullPath);
			string directoryName2 = Path.GetDirectoryName(fullPath2);
			if (!string.Equals(directoryName, directoryName2))
			{
				reporter.ReportMessage($"Error: source directory {fullPath} and destination directory {fullPath2} cannot contain each other");
				return false;
			}
		}
		if ((parameters.DeleteExcludeFiles != null || parameters.DeleteExcludeDirs != null) && !parameters.DeleteFromDest)
		{
			reporter.ReportMessage("Error: exclude-from-deletion options (-ndf and -ndd) require deletion (-d) enabled.");
			return false;
		}
		if (!Directory.Exists(srcDir))
		{
			reporter.ReportMessage("Error: source directory " + srcDir + " not found");
			return false;
		}
		return true;
	}

	private bool ProcessDirectory(string srcDir, string destDir, InputParams inputParams, ref SyncResults results, ProgressReporter reporter)
	{
		if (reporter.CancelRequested)
		{
			return false;
		}
		DirectoryInfo directoryInfo = new DirectoryInfo(srcDir);
		DirectoryInfo directoryInfo2 = new DirectoryInfo(destDir);
		if (!directoryInfo2.Exists)
		{
			try
			{
				reporter.ReportMessage("Creating directory: " + directoryInfo2.FullName);
				directoryInfo2.Create();
				results.DirectoriesCreated++;
			}
			catch (Exception ex)
			{
				reporter.ReportMessage("Error: failed to create directory " + destDir + ". " + ex.Message);
				return false;
			}
		}
		FileInfo[] files = GetFiles(directoryInfo, inputParams, ref results);
		FileInfo[] files2 = GetFiles(directoryInfo2, null, ref results);
		Hashtable hashtable = MakeHashtableFromFiles(files);
		Hashtable hashtable2 = MakeHashtableFromFiles(files2);
		FileInfo[] filesArray = files;
		foreach (FileInfo fileInfo in filesArray)
		{
			if (reporter.CancelRequested)
			{
				return false;
			}
			bool condition = false;
			FileInfo fileInfo2 = (FileInfo)hashtable2[fileInfo.Name];
			if (fileInfo2 != null && fileInfo.Length == fileInfo2.Length && fileInfo.LastWriteTime == fileInfo2.LastWriteTime && (IgnoreFileAttributeChanges || fileInfo.Attributes == fileInfo2.Attributes))
			{
				condition = true;
				results.FilesUpToDate++;
			}
			if (!condition)
			{
				string path = Path.Combine(destDir, fileInfo.Name);
				if (fileInfo2 != null && fileInfo2.IsReadOnly)
				{
					fileInfo2.IsReadOnly = false;
				}
				try
				{
					reporter.ReportMessage("Copying: " + fileInfo.FullName + " -> " + Path.GetFullPath(path));
					reporter.ReportMessage($"SrcAttrs:{fileInfo?.Attributes}, DstAttrs:{fileInfo2?.Attributes}");
					reporter.ReportMessage($"SrcWrite:{fileInfo?.LastWriteTime}, DstWrite:{fileInfo2?.LastWriteTime}");
					fileInfo.CopyTo(path, overwrite: true);
					File.SetAttributes(path, fileInfo.Attributes);
					results.FilesCopied++;
				}
				catch (Exception ex2)
				{
					reporter.ReportMessage($"Error: failed to copy file from {fileInfo.FullName} to {path}. {ex2.Message}");
					return false;
				}
			}
		}
		if (inputParams.DeleteFromDest)
		{
			filesArray = files2;
			foreach (FileInfo fileInfo3 in filesArray)
			{
				if (reporter.CancelRequested)
				{
					return false;
				}
				if ((FileInfo)hashtable[fileInfo3.Name] != null || ShouldExclude(inputParams.DeleteExcludeFiles, null, fileInfo3.Name))
				{
					continue;
				}
				Func<string, bool> shouldExcludeFile = inputParams.ShouldExcludeFile;
				if (shouldExcludeFile == null || !shouldExcludeFile(fileInfo3.FullName))
				{
					try
					{
						reporter.ReportMessage("Deleting: " + fileInfo3.FullName + " ");
						fileInfo3.IsReadOnly = false;
						fileInfo3.Delete();
						results.FilesDeleted++;
					}
					catch (Exception ex3)
					{
						reporter.ReportMessage("Error: failed to delete file from " + fileInfo3.FullName + ". " + ex3.Message);
						return false;
					}
				}
			}
		}
		DirectoryInfo[] directories = GetDirectories(directoryInfo, inputParams, ref results);
		DirectoryInfo[] directories2 = GetDirectories(directoryInfo2, null, ref results);
		Hashtable hashtable3 = new Hashtable(directories.Length);
		DirectoryInfo[] directoriesArray = directories;
		foreach (DirectoryInfo directoryInfo3 in directoriesArray)
		{
			hashtable3.Add(directoryInfo3.Name, directoryInfo3);
			if (!ProcessDirectory(directoryInfo3.FullName, Path.Combine(destDir, directoryInfo3.Name), inputParams, ref results, reporter))
			{
				return false;
			}
		}
		if (inputParams.DeleteFromDest)
		{
			directoriesArray = directories2;
			foreach (DirectoryInfo directoryInfo4 in directoriesArray)
			{
				if (reporter.CancelRequested)
				{
					return false;
				}
				if (!hashtable3.ContainsKey(directoryInfo4.Name) && !ShouldExclude(inputParams.DeleteExcludeDirs, null, directoryInfo4.Name))
				{
					try
					{
						reporter.ReportMessage("Deleting directory: " + directoryInfo4.FullName);
						DeleteDirectory(directoryInfo4);
						results.DirectoriesDeleted++;
					}
					catch (Exception ex4)
					{
						reporter.ReportMessage("Error: failed to delete directory " + directoryInfo4.FullName + ". " + ex4.Message);
						return false;
					}
				}
			}
		}
		return true;
	}

	private Hashtable MakeHashtableFromFiles(IList<FileInfo> files)
	{
		Hashtable hashtable = new Hashtable(files.Count);
		foreach (FileInfo file in files)
		{
			hashtable.Add(file.Name, file);
		}
		return hashtable;
	}

	private bool ShouldExclude(Regex[] excludeList, Regex[] includeList, string name)
	{
		if (excludeList != null)
		{
			Regex[] excludeListArray = excludeList;
			for (int i = 0; i < excludeListArray.Length; i++)
			{
				if (excludeListArray[i].Match(name).Success)
				{
					return true;
				}
			}
			return false;
		}
		if (includeList != null)
		{
			Regex[] includeListArray = includeList;
			for (int i = 0; i < includeListArray.Length; i++)
			{
				if (includeListArray[i].Match(name).Success)
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}
}
