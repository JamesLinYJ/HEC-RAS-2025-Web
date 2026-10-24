using System;

namespace Ras.Process.S3Commands;

internal static class S3Uri
{
	public const string Scheme = "s3://";

	public static bool TryParse(string uri, out string bucket, out string key)
	{
		bucket = null;
		key = null;
		if (string.IsNullOrEmpty(uri) || !uri.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		string bucket2 = uri.Substring("s3://".Length);
		int indexOf = bucket2.IndexOf('/');
		if (indexOf < 0)
		{
			bucket = bucket2;
			key = "";
			return !string.IsNullOrEmpty(bucket);
		}
		bucket = bucket2.Substring(0, indexOf);
		key = bucket2.Substring(indexOf + 1);
		return !string.IsNullOrEmpty(bucket);
	}

	public static string Format(string bucket, string key)
	{
		return "s3://" + bucket + "/" + key;
	}
}
