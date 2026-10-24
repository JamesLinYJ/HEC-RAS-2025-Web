using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.Progress;
using Utility.Validation;

namespace Utility.Files;

public static class FileHelpers
{
	private static readonly ILogger _logger = LogHelper.GetLogger("FileHelpers");

	private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();

	private static readonly char[] InvalidFilenameChars = Path.GetInvalidFileNameChars();

	private const int MAX_PATH = 4096;

	public const int DefaultRASHBlockSize = 1048576;

	private static readonly Dictionary<string, long> _unitMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
	{
		{ "", 1L },
		{ "b", 1L },
		{ "kb", 1000L },
		{ "mb", 1000000L },
		{ "gb", 1000000000L },
		{ "tb", 1000000000000L },
		{ "kib", 1024L },
		{ "mib", 1048576L },
		{ "gib", 1073741824L },
		{ "tib", 1099511627776L }
	};

	public static string UniqueFilename(string baseFilename)
	{
		if (!File.Exists(baseFilename))
		{
			return baseFilename;
		}
		string? directoryName = Path.GetDirectoryName(baseFilename);
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseFilename);
		string extension = Path.GetExtension(baseFilename);
		return UniqueFilename(directoryName, fileNameWithoutExtension, extension);
	}

	public static string UniqueFilename(string directory, string baseFilename, string extension)
	{
		string path = Path.Combine(directory, baseFilename + extension);
		if (!File.Exists(path))
		{
			return path;
		}
		for (int i = 1; i < 1000; i++)
		{
			string path2 = Path.Combine(directory, baseFilename + " (" + i + ")" + extension);
			if (!File.Exists(path2))
			{
				return path2;
			}
		}
		throw new Exception("Error - do you really have 1000 files with the same name in the same directory?");
	}

	public static string GetUniqueDirectory(string path)
	{
		return GetUniqueFileName(path, "", (string d) => Directory.Exists(d) || File.Exists(d));
	}

	public static LeveledValidationResult ValidateUniqueFile(string file)
	{
		if (!File.Exists(file))
		{
			return LeveledValidationResult.Success;
		}
		return new LeveledValidationResult("File already exists");
	}

	public static string GetUniqueFileName(string pathWithoutExtension, string extension, Func<string, bool> checkExists = null)
	{
		if (extension.Length > 0 && extension[0] != '.')
		{
			extension = extension.Insert(0, ".");
		}
		Func<string, bool> func = checkExists;
		if (func == null)
		{
			func = CanReadFile;
		}
		while (func(pathWithoutExtension + extension))
		{
			if (pathWithoutExtension[pathWithoutExtension.Length - 1] == ')')
			{
				int lengthOffset = pathWithoutExtension.Length - 2;
				string insertPathWithoutExtensionTerm = "";
				while (pathWithoutExtension[lengthOffset] != '(')
				{
					insertPathWithoutExtensionTerm = insertPathWithoutExtensionTerm.Insert(0, pathWithoutExtension[lengthOffset].ToString());
					lengthOffset--;
				}
				if (!int.TryParse(insertPathWithoutExtensionTerm, out var result))
				{
					pathWithoutExtension += " (1)";
					continue;
				}
				pathWithoutExtension = pathWithoutExtension.Remove(pathWithoutExtension.LastIndexOf(" (" + result + ")"));
				pathWithoutExtension = pathWithoutExtension + " (" + (result + 1) + ")";
			}
			else
			{
				pathWithoutExtension += " (1)";
			}
		}
		return pathWithoutExtension + extension;
	}

	public static string GetUniqueName(string baseName, Func<string, bool> checkExists)
	{
		if (checkExists == null)
		{
			throw new ArgumentNullException("checkExists");
		}
		string baseName2 = baseName;
		while (checkExists(baseName2))
		{
			if (baseName2.EndsWith(")") && baseName2.Contains("("))
			{
				int lastIndexOf = baseName2.LastIndexOf(')');
				int lastIndexOf2 = baseName2.LastIndexOf('(', lastIndexOf);
				if (lastIndexOf2 == -1 || lastIndexOf <= lastIndexOf2)
				{
					baseName2 += " (1)";
				}
				else
				{
					baseName2 = ((!int.TryParse(baseName2.Substring(lastIndexOf2 + 1, lastIndexOf - lastIndexOf2 - 1), out var result)) ? (baseName2 + " (1)") : (baseName2.Substring(0, lastIndexOf2) + $"({result + 1})"));
				}
			}
			else
			{
				baseName2 += " (1)";
			}
		}
		return baseName2;
	}

	public static string DirectoryHash(string directory, string fileSearchPattern)
	{
		if (!Directory.Exists(directory))
		{
			throw new ArgumentException();
		}
		string[] files = Directory.GetFiles(directory, fileSearchPattern, SearchOption.AllDirectories);
		Hash hash = new Hash();
		string[] filesArray = files;
		for (int i = 0; i < filesArray.Length; i++)
		{
			byte[] buf = File.ReadAllBytes(filesArray[i]);
			hash.Add(buf);
		}
		return hash.HashString();
	}

	public static string ComputeFileMD5(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException("filename");
		}
		using FileStream source = File.OpenRead(filename);
		return Convert.ToHexString(MD5.HashData(source));
	}

	public static string ComputeRashMD5(string filename, int blockSize = 1048576, ProgressReporter pr = null)
	{
		return Convert.ToHexString(ComputeRashMD5Bytes(filename, blockSize));
	}

	public static byte[] ComputeRashMD5Bytes(string filename, int blockSize = 1048576, ProgressReporter pr = null)
	{
		MD5.Create();
		long length = new FileInfo(filename).Length;
		if (length == 0L)
		{
			return MD5.HashData(Array.Empty<byte>());
		}
		if (blockSize >= length)
		{
			using (FileStream source = File.OpenRead(filename))
			{
				return MD5.HashData(source);
			}
		}
		bool report = true;
		if (pr == null)
		{
			report = false;
		}
		ThreadLocal<FileStream> streams = new ThreadLocal<FileStream>(() => new FileStream(filename, FileMode.Open, FileAccess.Read), trackAllValues: true);
		ThreadLocal<byte[]> buffers = new ThreadLocal<byte[]>(() => new byte[blockSize]);
		long blocksDone = 0L;
		try
		{
			long nBlocks = (long)Math.Ceiling((double)length / (double)blockSize);
			byte[] concatHashes = new byte[16 * nBlocks];
			System.Threading.Tasks.Parallel.For(0L, nBlocks, (long blockIdx) =>
			{
				long position = blockIdx * blockSize;
				FileStream fileStream = streams.Value;
				fileStream.Position = position;
				byte[] buffersTerm = buffers.Value;
				buffersTerm.Fill((byte)0);
				int length2 = fileStream.Read(buffersTerm);
				Span<byte> buffersTermSpan = buffersTerm.AsSpan(0, length2);
				MD5.HashData(destination: concatHashes.AsSpan((int)(blockIdx * 16), 16), source: buffersTermSpan);
				if (report)
				{
					lock (streams)
					{
						blocksDone++;
						pr.ReportProgressFraction((float)blockIdx / (float)nBlocks);
					}
				}
			});
			return MD5.HashData(concatHashes);
		}
		finally
		{
			if (streams != null)
			{
				foreach (FileStream fileStream2 in streams.Values)
				{
					fileStream2.Dispose();
				}
			}
		}
	}

	public static string ComputeRashXX3(string filename, int blockSize = 1048576, ProgressReporter pr = null)
	{
		return Convert.ToHexString(ComputeRashXX3Bytes(filename, blockSize, pr));
	}

	public static byte[] ComputeRashXX3Bytes(string filename, int blockSize = 1048576, ProgressReporter pr = null)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		long length = new FileInfo(filename).Length;
		if (length == 0L)
		{
			return new byte[8];
		}
		if (blockSize >= length)
		{
			byte[] allBytes = File.ReadAllBytes(filename);
			byte[] byteValues = new byte[8];
			XxHash3.Hash(allBytes, byteValues, 0L);
			return byteValues;
		}
		bool report = true;
		if (pr == null)
		{
			report = false;
		}
		ThreadLocal<FileStream> streams = new ThreadLocal<FileStream>(() => new FileStream(filename, FileMode.Open, FileAccess.Read), trackAllValues: true);
		ThreadLocal<byte[]> buffers = new ThreadLocal<byte[]>(() => new byte[blockSize]);
		long blocksDone = 0L;
		try
		{
			long nBlocks = length / blockSize;
			if (length % blockSize != 0L)
			{
				nBlocks++;
			}
			byte[] concatHashes = new byte[8 * nBlocks];
			System.Threading.Tasks.Parallel.For(0L, nBlocks, (long blockIdx) =>
			{
				long position = blockIdx * blockSize;
				FileStream fileStream = streams.Value;
				fileStream.Position = position;
				byte[] buffersTerm = buffers.Value;
				buffersTerm.Fill((byte)0);
				int length2 = fileStream.Read(buffersTerm);
				Span<byte> buffersTermSpan = buffersTerm.AsSpan(0, length2);
				XxHash3.Hash(destination: concatHashes.AsSpan((int)(blockIdx * 8), 8), source: buffersTermSpan, seed: 0L);
				if (report)
				{
					lock (streams)
					{
						blocksDone++;
						pr.ReportProgressFraction((float)blockIdx / (float)nBlocks);
					}
				}
			});
			byte[] byteValuesArray = new byte[8];
			XxHash3.Hash(concatHashes, byteValuesArray, 0L);
			return byteValuesArray;
		}
		finally
		{
			if (streams != null)
			{
				foreach (FileStream fileStream2 in streams.Values)
				{
					fileStream2.Dispose();
				}
			}
		}
	}

	public static string ComputeFileSHA256(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException("filename");
		}
		using FileStream source = new FileStream(filename, FileMode.Open, FileAccess.Read);
		return Convert.ToHexString(SHA256.HashData(source));
	}

	public static async Task<string> ComputeFileSHA256Async(string filename, ProgressReporter pr = null)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException("filename");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		using FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
		using SHA256 hs = SHA256.Create();
		byte[] buffer = new byte[4096];
		long totalBytes = 0L;
		long fileLen = fs.Length;
		int readAsync;
		while (true)
		{
			readAsync = await fs.ReadAsync(buffer);
			totalBytes += readAsync;
			if (readAsync < 4096)
			{
				break;
			}
			hs.TransformBlock(buffer, 0, 4096, null, 0);
			pr.ReportProgressFraction((float)totalBytes / (float)fileLen);
		}
		if (readAsync != 0)
		{
			hs.TransformFinalBlock(buffer, 0, readAsync);
		}
		byte[]? hash = hs.Hash;
		pr.ReportProgress(100);
		return Convert.ToHexString(hash);
	}

	public static async Task<string> ComputeFileMD5Async(string filename)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException("filename");
		}
		using FileStream fs = new FileStream(filename, FileMode.Open, FileAccess.Read);
		return Convert.ToHexString(await MD5.HashDataAsync(fs));
	}

	public static void EnsurePathExists(string filename)
	{
		string directoryName = Path.GetDirectoryName(filename);
		if (!Directory.Exists(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
	}

	public static FileStream WaitForFile(string fullPath, FileMode mode, FileAccess access, FileShare share, int count = 20, int sleepBetweenTries = 50)
	{
		for (int i = 0; i < count; i++)
		{
			FileStream fileStream = null;
			try
			{
				fileStream = new FileStream(fullPath, mode, access, share);
				return fileStream;
			}
			catch (IOException)
			{
				fileStream?.Dispose();
				Thread.Sleep(50);
			}
		}
		return null;
	}

	public static bool IsFileWritable(string filename)
	{
		try
		{
			bool canReadFile = CanReadFile(filename);
			using (new FileStream(filename, FileMode.Append, FileAccess.Write, FileShare.None))
			{
			}
			if (!canReadFile)
			{
				TryDelete(filename);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static bool TryCopy(string baseFilename, string destFilename)
	{
		try
		{
			if (!CanReadFile(baseFilename))
			{
				return false;
			}
			if (TryDelete(destFilename))
			{
				File.Copy(baseFilename, destFilename);
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool TryDelete(string fullPath, int timeoutMS = 1000)
	{
		if (timeoutMS <= 0)
		{
			timeoutMS = 1;
		}
		if (!File.Exists(fullPath))
		{
			return true;
		}
		int count = (int)Math.Ceiling((double)timeoutMS / 50.0);
		for (int i = 0; i < count; i++)
		{
			try
			{
				File.Delete(fullPath);
				return true;
			}
			catch
			{
				Thread.Sleep(50);
			}
		}
		return false;
	}

	public static async Task<bool> TryDeleteAsync(string fullPath, int timeoutMS = 1000)
	{
		if (timeoutMS <= 0)
		{
			timeoutMS = 1;
		}
		int numX = (int)Math.Ceiling((double)timeoutMS / 50.0);
		for (int i = 0; i < numX; i++)
		{
			try
			{
				File.Delete(fullPath);
				return true;
			}
			catch
			{
				await Task.Delay(50);
			}
		}
		return false;
	}

	public static bool WaitUntilFileIsWriteable(string fullPath, int timeoutMS = 1000)
	{
		if (timeoutMS <= 0)
		{
			timeoutMS = 1;
		}
		int count = (int)Math.Ceiling((double)timeoutMS / 50.0);
		for (int i = 0; i < count; i++)
		{
			try
			{
				using (File.OpenWrite(fullPath))
				{
					return true;
				}
			}
			catch
			{
				Thread.Sleep(50);
			}
		}
		return false;
	}

	public static async Task<bool> WaitUntilFileIsWriteableAsync(string fullPath, int timeoutMS = 1000)
	{
		if (timeoutMS <= 0)
		{
			timeoutMS = 1;
		}
		int numX = (int)Math.Ceiling((double)timeoutMS / 50.0);
		for (int i = 0; i < numX; i++)
		{
			try
			{
				using (File.OpenWrite(fullPath))
				{
					return true;
				}
			}
			catch
			{
				await Task.Delay(50);
			}
		}
		return false;
	}

	public static bool TestCanDelete(string path)
	{
		try
		{
			string tempFileName = Path.GetTempFileName();
			File.Move(path, tempFileName, overwrite: true);
			File.Move(tempFileName, path, overwrite: true);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static string MakeRelative(string relativeToDirectory, string targetPath)
	{
		if (!Path.IsPathRooted(targetPath))
		{
			throw new ArgumentException(targetPath.InDoubleQuotes() + " is not rooted.");
		}
		if (!relativeToDirectory.EndsWith("/") && !relativeToDirectory.EndsWith("\\"))
		{
			relativeToDirectory += Path.DirectorySeparatorChar;
		}
		return Path.GetRelativePath(relativeToDirectory, targetPath);
	}

	public static string MakeRelative(string relativeToDirectory, string targetPath, out bool isRelative)
	{
		string relative = MakeRelative(relativeToDirectory, targetPath);
		isRelative = !Path.IsPathFullyQualified(relative);
		return relative;
	}

	public static string MakeAbsolute(string baseDirectory, string relativePath)
	{
		if (Path.IsPathFullyQualified(relativePath))
		{
			return relativePath;
		}
		return Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
	}

	public static string SanitizeForOS(string path)
	{
		path = ((!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? path.Replace("\\", "/") : path.Replace("/", "\\"));
		return path;
	}

	public static string SanitizePath(string name)
	{
		char[] invalidPathChars = InvalidPathChars;
		foreach (char oldChar in invalidPathChars)
		{
			name = name.Replace(oldChar, '_');
		}
		return name;
	}

	public static string SanitizeFilename(string name)
	{
		char[] invalidFilenameChars = InvalidFilenameChars;
		foreach (char oldChar in invalidFilenameChars)
		{
			name = name.Replace(oldChar, '_');
		}
		return name;
	}

	public static string MakeRelativeOLD(string filename, string baseFilename)
	{
		if (string.IsNullOrEmpty(filename))
		{
			return "";
		}
		string path = "";
		string directoryName = Path.GetDirectoryName(baseFilename);
		string directoryName2 = Path.GetDirectoryName(filename);
		if (directoryName == directoryName2)
		{
			return Path.Combine(".", Path.GetFileName(filename));
		}
		string[] parts = directoryName2.Split(Path.DirectorySeparatorChar);
		string[] partsArray = directoryName.Split(Path.DirectorySeparatorChar);
		int ordinal = -1;
		for (int i = 0; i < partsArray.Length && i <= parts.Length - 1 && partsArray[i].RoughlyEquals(parts[i]); i++)
		{
			ordinal = i;
		}
		if (ordinal < 0)
		{
			return filename;
		}
		if (ordinal == partsArray.Count() - 1)
		{
			path = ".";
		}
		else
		{
			for (int j = ordinal + 1; j < partsArray.Length; j++)
			{
				path = Path.Combine(path, "..");
			}
		}
		for (int k = ordinal + 1; k < parts.Length; k++)
		{
			path = Path.Combine(path, parts[k]);
		}
		return Path.Combine(path, Path.GetFileName(filename));
	}

	public static string MakeAbsoluteOLD(string filename, string baseFilename)
	{
		if (string.IsNullOrEmpty(filename))
		{
			return "";
		}
		string directoryName = Path.GetDirectoryName(baseFilename);
		string[] parts = directoryName.Split(Path.DirectorySeparatorChar);
		string[] partsArray = filename.Split(Path.DirectorySeparatorChar);
		string directoryNameDirectorySeparatorCharTerm;
		if (partsArray.Length == 1)
		{
			directoryNameDirectorySeparatorCharTerm = directoryName + Path.DirectorySeparatorChar + filename;
		}
		else if (partsArray[0] == ".")
		{
			directoryNameDirectorySeparatorCharTerm = directoryName;
			for (int i = 1; i < partsArray.Length; i++)
			{
				directoryNameDirectorySeparatorCharTerm = Path.Combine(directoryNameDirectorySeparatorCharTerm, partsArray[i]);
			}
		}
		else if (partsArray[0] == "..")
		{
			int ordinal = -1;
			for (int j = 0; j <= partsArray.Count() - 1 && partsArray[j] == ".."; j++)
			{
				ordinal = j;
			}
			directoryNameDirectorySeparatorCharTerm = Path.GetPathRoot(directoryName);
			ordinal++;
			for (int k = 1; k < parts.Length - ordinal; k++)
			{
				directoryNameDirectorySeparatorCharTerm = Path.Combine(directoryNameDirectorySeparatorCharTerm, parts[k]);
			}
			for (int l = ordinal; l < partsArray.Length; l++)
			{
				directoryNameDirectorySeparatorCharTerm = Path.Combine(directoryNameDirectorySeparatorCharTerm, partsArray[l]);
			}
		}
		else
		{
			directoryNameDirectorySeparatorCharTerm = filename;
		}
		return directoryNameDirectorySeparatorCharTerm;
	}

	public static string PrettyByteCount(long numBytes)
	{
		if (numBytes > 1073741824)
		{
			return Math.Round((decimal)numBytes / 1073741824m, 3) + " GB";
		}
		if (numBytes > 1048576)
		{
			return Math.Round((double)numBytes / 1048576.0, 3) + " MB";
		}
		if (numBytes > 1024)
		{
			return Math.Round((double)numBytes / 1024.0, 3) + " KB";
		}
		return numBytes + " Bytes";
	}

	public static async Task CopyToAsync(this Stream source, Stream destination, CancellationToken cancellationToken = default(CancellationToken), int bufferSize = 4096)
	{
		byte[] buffer = new byte[bufferSize];
		int totalRead = 0;
		while (true)
		{
			int readAsync;
			int bytesRead = (readAsync = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken));
			if (readAsync <= 0)
			{
				break;
			}
			await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			totalRead += bytesRead;
		}
	}

	public static bool IsValidPath(string path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			return path.IndexOfAny(InvalidPathChars) == -1;
		}
		return false;
	}

	public static bool IsValidFilename(string filename)
	{
		if (!string.IsNullOrWhiteSpace(filename))
		{
			return filename.IndexOfAny(Path.GetInvalidFileNameChars().ToArray()) == -1;
		}
		return false;
	}

	public static bool IsValidAbsolutePath(string path)
	{
		if (!string.IsNullOrWhiteSpace(path) && path.IndexOfAny(Path.GetInvalidPathChars().ToArray()) == -1 && Path.IsPathRooted(path))
		{
			return !Path.GetPathRoot(path).Equals(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
		}
		return false;
	}

	public static bool CanReadFile(string path)
	{
		FileState status;
		string message;
		return CanReadFile(path, out status, out message);
	}

	public static bool CanReadFile(string path, out FileState status, out string message)
	{
		if (path != null && path.Length >= 4096)
		{
			status = FileState.PathTooLong;
			message = "File path exceeds max allowable characters in Windows: " + 4096;
			return false;
		}
		try
		{
			if (File.Exists(path))
			{
				status = FileState.Exists;
				message = "";
				return true;
			}
			status = FileState.FileNotFound;
			message = "File not found.";
			return false;
		}
		catch (DriveNotFoundException)
		{
			status = FileState.DriveLetterNotFound;
			message = "Drive not found.";
			return false;
		}
		catch (Exception ex2)
		{
			status = FileState.UnknownError;
			message = ex2.Message;
			return false;
		}
	}

	public static List<string> CondenseFilenames(IList<string> absolutePaths)
	{
		IList<string> absolutePathsList = new List<string>(absolutePaths);
		List<string> stringList = new List<string>();
		foreach (string absolutePath2 in absolutePathsList)
		{
			string directoryName = Path.GetDirectoryName(absolutePath2);
			if (directoryName != null && !directoryName.EndsWith(Path.DirectorySeparatorChar.ToString()))
			{
				directoryName += Path.DirectorySeparatorChar;
			}
			stringList.Add(directoryName);
		}
		bool condition = false;
		while (stringList.Count > 0 && !condition)
		{
			stringList.Clear();
			foreach (string absolutePath3 in absolutePathsList)
			{
				string directoryName2 = Path.GetDirectoryName(absolutePath3);
				if (directoryName2 != null && !directoryName2.EndsWith(Path.DirectorySeparatorChar.ToString()))
				{
					directoryName2 += Path.DirectorySeparatorChar;
				}
				stringList.Add(directoryName2);
			}
			if (stringList.Distinct().Count() == 1)
			{
				List<string> stringListList = new List<string>();
				{
					foreach (string absolutePath in absolutePaths)
					{
						StringBuilder stringBuilder = new StringBuilder();
						if (!condition)
						{
							stringBuilder.Append("..." + Path.DirectorySeparatorChar);
						}
						stringBuilder.Append(absolutePath.RemoveFirstInstance(stringList.First()));
						stringListList.Add(stringBuilder.ToString());
					}
					return stringListList;
				}
			}
			for (int i = 0; i < absolutePathsList.Count; i++)
			{
				if (!absolutePathsList[i].Contains(Path.DirectorySeparatorChar))
				{
					condition = true;
				}
				else
				{
					absolutePathsList[i] = stringList[i].Substring(0, stringList[i].Length - 1);
				}
			}
		}
		return absolutePaths.ToList();
	}

	private static bool FileExistsLongInternal(string path)
	{
		string[] parts = path.Split('\\');
		StringBuilder stringBuilder = new StringBuilder(parts[0]);
		for (int i = 1; i < parts.Length; i++)
		{
			if (stringBuilder.Length + parts[i].Length >= 4096)
			{
				parts = parts.Skip(i).ToArray();
				break;
			}
			stringBuilder.Append("\\" + parts[i]);
		}
		DirectoryInfo directoryInfo = new DirectoryInfo(stringBuilder.ToString());
		bool exists = directoryInfo.Exists;
		if (exists)
		{
			for (int j = 0; (j < parts.Length - 1) & exists; j++)
			{
				exists = false;
				DirectoryInfo[] directories = directoryInfo.GetDirectories();
				foreach (DirectoryInfo directoryInfo2 in directories)
				{
					if (directoryInfo2.Name == parts[j])
					{
						directoryInfo = directoryInfo2;
						exists = true;
						break;
					}
				}
			}
			if (exists)
			{
				exists = false;
				FileInfo[] files = directoryInfo.GetFiles();
				for (int k = 0; k < files.Length; k++)
				{
					if (files[k].Name == parts[^1])
					{
						exists = true;
						break;
					}
				}
			}
		}
		return exists;
	}

	public static void OpenFolderAndSelect(string fileOrDirectory)
	{
		LaunchExplorerAndSelect(fileOrDirectory);
	}

	public static void LaunchExplorerAndSelect(string fileOrDirectory)
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			if (Directory.Exists(fileOrDirectory))
			{
				LaunchDirectory(fileOrDirectory);
			}
			else
			{
				LaunchExplorerAndSelect_Windows(fileOrDirectory);
			}
		}
		else
		{
			DesktopFileActions.Open(fileOrDirectory, reveal: true);
		}
	}

	public static void LaunchDirectory(string directory)
	{
		if (!Directory.Exists(directory))
		{
			throw new ArgumentException("Path must be a valid directory.");
		}
		DesktopFileActions.Open(directory);
	}

	private static void LaunchExplorerAndSelect_Windows(string filePath)
	{
		if (string.IsNullOrEmpty(filePath))
		{
			throw new ArgumentNullException("filePath");
		}
		nint ilCreateFromPathW = ILCreateFromPathW(filePath);
		SHOpenFolderAndSelectItems(ilCreateFromPathW, 0, IntPtr.Zero, 0);
		ILFree(ilCreateFromPathW);
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern nint ILCreateFromPathW(string pszPath);

	[DllImport("shell32.dll")]
	private static extern int SHOpenFolderAndSelectItems(nint pidlFolder, int cild, nint apidl, int dwFlags);

	[DllImport("shell32.dll")]
	private static extern void ILFree(nint pidl);

	public static void LaunchWithDefaultViewer(string fn)
	{
		string extension = Path.GetExtension(fn);
		if (extension.RoughlyEquals(".txt") || extension.RoughlyEquals(".log"))
		{
			OpenTextFileWithDefaultViewer(fn);
		}
		else
		{
			DesktopFileActions.Open(fn);
		}
	}

	public static void OpenTextFileWithDefaultViewer(string fn, bool tryScrollToEnd = false)
	{
		// The viewer is the user's system association on every desktop platform.
		// Scrolling is viewer-specific; the common desktop API has no such flag.
		DesktopFileActions.Open(fn);
	}

	public static bool ExistsOnPath(string fileName)
	{
		return GetFullPath(fileName) != null;
	}

	public static string GetFullPath(string fileName)
	{
		if (File.Exists(fileName))
		{
			return Path.GetFullPath(fileName);
		}
		string[] parts = Environment.GetEnvironmentVariable("PATH").Split(Path.PathSeparator);
		for (int i = 0; i < parts.Length; i++)
		{
			string path = Path.Combine(parts[i], fileName);
			if (File.Exists(path))
			{
				return path;
			}
		}
		return null;
	}

	public static int CountFilesInDirectory(string basePath, string folderName)
	{
		return CountFilesInDirectory(Path.Combine(basePath, folderName));
	}

	public static int CountFilesInDirectory(string path)
	{
		if (!Directory.Exists(path))
		{
			return 0;
		}
		return Directory.GetFiles(path).Length;
	}

	public static OperationResult TryParseAsFileSize(string input, out long sizeInBytes)
	{
		if (string.IsNullOrEmpty(input))
		{
			sizeInBytes = 0L;
			return OperationResult.Fail("Input is null.");
		}
		string trim = input.Trim();
		if (trim.Length == 0)
		{
			sizeInBytes = 0L;
			return OperationResult.Fail("Input is empty/whitespace.");
		}
		int i;
		for (i = 0; i < trim.Length; i++)
		{
			char c = trim[i];
			if ((c < '0' || c > '9') && c != '.' && c != '+' && c != '-' && c != 'e' && c != 'E')
			{
				break;
			}
		}
		string trim2 = trim.Substring(0, i).Trim();
		if (trim2.Length == 0)
		{
			sizeInBytes = 0L;
			return OperationResult.Fail(new Message("No numeric value found in input: {InputText}", input));
		}
		string trim3 = trim.Substring(i).Trim();
		string c2Sum = "";
		string trim4 = trim3;
		foreach (char c2 in trim4)
		{
			if (char.IsLetter(c2))
			{
				c2Sum += c2;
			}
		}
		if (!double.TryParse(trim2, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			sizeInBytes = 0L;
			return OperationResult.Fail(new Message("Invalid numeric value found in input: {InputText}", input));
		}
		if (!_unitMap.TryGetValue(c2Sum, out var localValue))
		{
			sizeInBytes = 0L;
			return OperationResult.Fail(new Message("Unknown unit: {UnitText}", trim3));
		}
		double sizeInBytes2 = result * (double)localValue;
		if (sizeInBytes2 < 0.0 || sizeInBytes2 > 9.223372036854776E+18)
		{
			sizeInBytes = 0L;
			return OperationResult.Fail(new Message("Input value \"{BytesDouble}\" is out of range.", sizeInBytes2));
		}
		sizeInBytes = (long)sizeInBytes2;
		return true;
	}

	public static string WriteIncompressibleFile(long fileSizeBytes)
	{
		string tempFileName = Path.GetTempFileName();
		using FileStream writer = new FileStream(tempFileName, FileMode.Append, FileAccess.Write);
		WriteIncompressibleFileInternal(writer, fileSizeBytes);
		return tempFileName;
	}

	public static void WriteIncompressibleFile(string fn, long fileSizeBytes)
	{
		using FileStream writer = new FileStream(fn, FileMode.Create, FileAccess.Write);
		WriteIncompressibleFileInternal(writer, fileSizeBytes);
	}

	private static void WriteIncompressibleFileInternal(FileStream writer, long fileSizeBytes)
	{
		byte[] buffer = new byte[1048576];
		Random.Shared.NextBytes(buffer);
		long ordinal = 0L;
		do
		{
			long fileSizeBytesTerm = fileSizeBytes - ordinal;
			int count = (int)Math.Min(1048576L, fileSizeBytesTerm);
			writer.Write(buffer, 0, count);
			ordinal += 1048576;
		}
		while (ordinal < fileSizeBytes);
	}

	public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(sourceDir);
		if (!directoryInfo.Exists)
		{
			throw new DirectoryNotFoundException("Source directory not found: " + directoryInfo.FullName);
		}
		DirectoryInfo[] directories = directoryInfo.GetDirectories();
		Directory.CreateDirectory(destinationDir);
		FileInfo[] files = directoryInfo.GetFiles();
		foreach (FileInfo fileInfo in files)
		{
			string destFileName = Path.Combine(destinationDir, fileInfo.Name);
			fileInfo.CopyTo(destFileName);
		}
		if (recursive)
		{
			DirectoryInfo[] directoriesArray = directories;
			foreach (DirectoryInfo directoryInfo2 in directoriesArray)
			{
				string destinationDir2 = Path.Combine(destinationDir, directoryInfo2.Name);
				CopyDirectory(directoryInfo2.FullName, destinationDir2, recursive: true);
			}
		}
	}
}
