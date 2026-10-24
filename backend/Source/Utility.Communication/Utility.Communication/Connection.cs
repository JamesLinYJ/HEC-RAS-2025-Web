using System;
using System.Collections.ObjectModel;
using System.Linq;
using Utility.Extensions;

namespace Utility.Communication;

public class Connection
{
	public string URL { get; set; }

	public string BucketSubdomain { get; set; }

	public Protocol Protocol { get; set; }

	public Authentication Authentication { get; set; }

	public ObservableCollection<string> ExcludedSubdirectories { get; set; } = new ObservableCollection<string>();

	public string FriendlyName { get; set; }

	public string Serialize()
	{
		return SanitizeSaving(FriendlyName) + "\n" + ProtocolToString(Protocol) + "\n" + SanitizeSaving(URL) + "\n" + SanitizeSaving(BucketSubdomain) + "\n" + SanitizeSaving(Authentication.UserName) + "\n" + SanitizeSaving(Authentication.Password) + "\n" + ExcludedDirsSerialized();
	}

	public static Connection TryDeserialize(string sl)
	{
		string[] parts = sl.Split(new char[1] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length >= 6)
		{
			string friendlyName = SanitizeLoading(parts[0]);
			Protocol protocol = ParseProtocol(parts[1]);
			string uRL = SanitizeLoading(parts[2]);
			string bucketSubdomain = SanitizeLoading(parts[3]);
			string userName = SanitizeLoading(parts[4]);
			string password = SanitizeLoading(parts[5]);
			ObservableCollection<string> excludedSubdirectories = new ObservableCollection<string>();
			if (parts.Length > 6)
			{
				excludedSubdirectories = ExcludedDirsDeserialized(parts[6]);
			}
			return new Connection
			{
				FriendlyName = friendlyName,
				URL = uRL,
				BucketSubdomain = bucketSubdomain,
				Protocol = protocol,
				ExcludedSubdirectories = excludedSubdirectories,
				Authentication = new Authentication
				{
					UserName = userName,
					Password = password
				}
			};
		}
		return null;
	}

	private string ExcludedDirsSerialized()
	{
		string textValue = "";
		bool condition = true;
		foreach (string excludedSubdirectory in ExcludedSubdirectories)
		{
			if (!condition)
			{
				textValue += "?!?";
			}
			textValue += excludedSubdirectory;
			condition = false;
		}
		return textValue;
	}

	private static ObservableCollection<string> ExcludedDirsDeserialized(string textValue)
	{
		ObservableCollection<string> observableCollection = new ObservableCollection<string>();
		string[] parts = textValue.Split(new string[1] { "?!?" }, StringSplitOptions.RemoveEmptyEntries);
		if (parts != null)
		{
			observableCollection.AddRange(parts);
		}
		return observableCollection;
	}

	private static string SanitizeSaving(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "_";
		}
		return s;
	}

	private static string SanitizeLoading(string s)
	{
		if (s == "_")
		{
			return "";
		}
		return s;
	}

	public static Protocol ParseProtocol(string pc)
	{
		if (pc.RoughlyEquals("fs"))
		{
			return Protocol.FileSystem;
		}
		if (pc.RoughlyEquals("s3"))
		{
			return Protocol.S3;
		}
		if (pc.RoughlyEquals("sftp"))
		{
			return Protocol.SFTP;
		}
		return Protocol.Unknown;
	}

	public string ProtocolToString(Protocol pc)
	{
		return pc switch
		{
			Protocol.FileSystem => "fs", 
			Protocol.S3 => "s3", 
			Protocol.SFTP => "sftp", 
			_ => "unknown", 
		};
	}

	public string URLWithoutProtocol()
	{
		if (URL.Contains("//"))
		{
			return URL.Split(new string[1] { "//" }, StringSplitOptions.RemoveEmptyEntries).Last();
		}
		return URL;
	}

	public Connection Clone()
	{
		return new Connection
		{
			URL = URL,
			BucketSubdomain = BucketSubdomain,
			Protocol = Protocol,
			Authentication = Authentication.Clone(),
			ExcludedSubdirectories = new ObservableCollection<string>(ExcludedSubdirectories),
			FriendlyName = FriendlyName
		};
	}
}
