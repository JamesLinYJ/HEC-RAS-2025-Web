using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Testing.Core.Regression;

public static class ContentHashing
{
	public static string HashFile(string path)
	{
		using FileStream inputStream = File.OpenRead(path);
		using SHA256 sHA = SHA256.Create();
		return Convert.ToHexString(sHA.ComputeHash(inputStream)).ToLowerInvariant();
	}

	public static string HashDirectory(string root)
	{
		string fullPath = Path.GetFullPath(root);
		string[] files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
		List<(string, string)> valueTuples = new List<(string, string)>(files.Length);
		string[] filesArray = files;
		foreach (string filesArray2 in filesArray)
		{
			valueTuples.Add((NormalizeRelativePath(fullPath, filesArray2), filesArray2));
		}
		valueTuples.Sort(((string Key, string Path) a, (string Key, string Path) b) => string.CompareOrdinal(a.Key, b.Key));
		using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		byte[] byteValues = new byte[81920];
		foreach (var (s, localValue) in valueTuples)
		{
			incrementalHash.AppendData(Encoding.UTF8.GetBytes(s));
			incrementalHash.AppendData(BitConverter.GetBytes(new FileInfo(localValue).Length));
			using FileStream fileStream = File.OpenRead(localValue);
			int length;
			while ((length = fileStream.Read(byteValues, 0, byteValues.Length)) > 0)
			{
				incrementalHash.AppendData(byteValues.AsSpan(0, length));
			}
		}
		return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
	}

	private static string NormalizeRelativePath(string root, string file)
	{
		return Path.GetRelativePath(root, file).Replace('\\', '/').ToLowerInvariant();
	}
}
