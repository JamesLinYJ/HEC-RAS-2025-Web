using System;
using System.IO;
using System.Xml.Linq;
using Utility;

namespace Testing.Core.Regression;

public readonly struct DevAppDataCredentials
{
	public const string SuperSecretRasDecryptionKey = "ras25decryptNOW";

	public const string AppDataFilename = "Ras.Interface.WinUI.xml";

	public const string DevS3KeyKey = "DevU";

	public const string DevS3SecretKey = "DevP";

	public const string TestingUtilityUrlKey = "TestingUtilityUrl";

	public string S3Key { get; init; }

	public string S3Secret { get; init; }

	public string TestingUtilityUrl { get; init; }

	public static bool TryRead(out DevAppDataCredentials credentials)
	{
		credentials = Empty();
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ras.Interface.WinUI.xml");
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			XElement root = XElement.Parse(File.ReadAllText(path));
			credentials = new DevAppDataCredentials
			{
				S3Key = ReadNode(root, "DevU"),
				S3Secret = Decrypt(ReadNode(root, "DevP")),
				TestingUtilityUrl = ReadNode(root, "TestingUtilityUrl")
			};
			return true;
		}
		catch
		{
			credentials = Empty();
			return false;
		}
	}

	private static DevAppDataCredentials Empty()
	{
		return new DevAppDataCredentials
		{
			S3Key = "",
			S3Secret = "",
			TestingUtilityUrl = ""
		};
	}

	private static string ReadNode(XElement root, string key)
	{
		foreach (XElement element in root.Elements())
		{
			if (element.Attribute("key")?.Value == key)
			{
				return element.Value;
			}
		}
		return "";
	}

	private static string Decrypt(string encrypted)
	{
		if (string.IsNullOrEmpty(encrypted))
		{
			return "";
		}
		try
		{
			return EncryptionHelpers.DecryptStringFromBase64(encrypted, "ras25decryptNOW");
		}
		catch
		{
			return "";
		}
	}
}
