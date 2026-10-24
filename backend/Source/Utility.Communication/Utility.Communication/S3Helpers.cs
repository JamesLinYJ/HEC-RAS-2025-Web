using System.Collections.Generic;
using System.IO;

namespace Utility.Communication;

public static class S3Helpers
{
	public static List<S3Client> TryParseCredentialFile(string filename)
	{
		if (!File.Exists(filename))
		{
			return new List<S3Client>();
		}
		List<S3Client> s3Clients = new List<S3Client>();
		string[] lines = File.ReadAllLines(filename);
		int cur = 0;
		while (true)
		{
			string nextRealLine = NextRealLine();
			if (string.IsNullOrEmpty(nextRealLine))
			{
				return s3Clients;
			}
			string nextRealLine2 = NextRealLine();
			if (string.IsNullOrEmpty(nextRealLine2))
			{
				return s3Clients;
			}
			string nextRealLine3 = NextRealLine();
			if (string.IsNullOrEmpty(nextRealLine3))
			{
				break;
			}
			s3Clients.Add(new S3Client(nextRealLine, nextRealLine2, nextRealLine3, forcePathStyleQueries: true));
		}
		return s3Clients;
		string NextRealLine()
		{
			while (cur < lines.Length)
			{
				string trim = lines[cur].Trim();
				cur++;
				if (!string.IsNullOrEmpty(trim) && !trim.StartsWith('#'))
				{
					return trim;
				}
			}
			return "";
		}
	}

	public static bool TryParseKeyFile(string filename, out string server, out string bucket, out string key)
	{
		if (!File.Exists(filename))
		{
			server = "";
			bucket = "";
			key = "";
			return false;
		}
		string[] allLines = File.ReadAllLines(filename);
		if (allLines.Length < 3)
		{
			server = "";
			bucket = "";
			key = "";
			return false;
		}
		server = allLines[0];
		bucket = allLines[1];
		key = allLines[2];
		return true;
	}

	public static string SanitizeS3Key(string key)
	{
		return key?.Replace(' ', '-') ?? "";
	}
}
