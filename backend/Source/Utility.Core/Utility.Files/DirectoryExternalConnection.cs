using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Utility.Logging;

namespace Utility.Files;

public class DirectoryExternalConnection : PathExternalConnection
{
	public const string TypeName = "Directory";

	public const bool RecurseDefault = false;

	protected override string Type => "Directory";

	public string SearchPattern { get; set; }

	public bool Recurse { get; set; }

	public DirectoryExternalConnection(string relativeToPath)
		: base(relativeToPath)
	{
		Recurse = false;
	}

	public override OperationResult ResourceExists()
	{
		return TryGetDirectory(out _);
	}

	public override OperationResult TryGetHash(out string hash)
	{
		hash = "";
		OperationResult result = TryGetFiles(out string[] files);
		if (!result) return result;
		try
		{
			TryGetDirectory(out string directory);
			using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			foreach (string file in files)
			{
				byte[] relative = Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace(Path.DirectorySeparatorChar, '/'));
				digest.AppendData(BitConverter.GetBytes(relative.Length)); digest.AppendData(relative);
				using Stream input = File.OpenRead(file);
				digest.AppendData(SHA256.HashData(input));
			}
			hash = Convert.ToHexString(digest.GetHashAndReset());
			return true;
		}
		catch (Exception error) { return OperationResult.Fail("Reading directory connection", error); }
	}

	public override OperationResult TryGetVersion(out int version)
	{
		version = -1;
		OperationResult result = TryGetHash(out string hash);
		if (!result) return result;
		byte[] bytes = Convert.FromHexString(hash);
		version = bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24;
		return true;
	}

	public OperationResult TryGetFiles(out string[] filenames)
	{
		filenames = Array.Empty<string>();
		OperationResult result = TryGetDirectory(out string directory);
		if (!result) return result;
		try
		{
			string pattern = string.IsNullOrEmpty(SearchPattern) ? "*" : SearchPattern;
			if (pattern.Contains('/') || pattern.Contains('\\')) return OperationResult.Fail("Enter a filename pattern without path separators.");
			filenames = Directory.EnumerateFiles(directory, pattern, new EnumerationOptions {
				RecurseSubdirectories = Recurse, IgnoreInaccessible = false,
				MatchCasing = MatchCasing.CaseInsensitive,
				AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false
			}).OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal).ToArray();
			return true;
		}
		catch (Exception error) { return OperationResult.Fail("Reading directory connection", error); }
	}

	public OperationResult TryGetDirectory(out string directory)
	{
		directory = "";
		try
		{
			if (!string.IsNullOrEmpty(RelativePath) && !Path.IsPathFullyQualified(RelativePath) && !string.IsNullOrEmpty(PathRelativeSourceDirectory))
			{
				string relative = Path.GetFullPath(RelativePath, PathRelativeSourceDirectory);
				if (Directory.Exists(relative)) { directory = relative; return true; }
			}
			if (!string.IsNullOrEmpty(AbsolutePath) && Directory.Exists(AbsolutePath)) { directory = Path.GetFullPath(AbsolutePath); return true; }
			return OperationResult.Fail("The directory connection could not be found.");
		}
		catch (Exception error) { return OperationResult.Fail("Resolving directory connection", error); }
	}

	public override void TryReconcilePaths()
	{
		string absPath = AbsolutePath;
		string relPath = RelativePath;
		ExternalConnection.TryReconcile(ref absPath, ref relPath, PathRelativeSourceDirectory, Directory.Exists);
		AbsolutePath = absPath;
		RelativePath = relPath;
	}

	public override void SaveXML(XElement rootXElement)
	{
		base.SaveXML(rootXElement);
		if (!string.IsNullOrEmpty(SearchPattern))
		{
			XElement xElement = new XElement("SearchPattern");
			rootXElement.Add(xElement);
			xElement.Value = SearchPattern;
		}
		XElement xElement2 = new XElement("Recurse");
		rootXElement.Add(xElement2);
		xElement2.Value = Recurse.ToString();
	}

	public override void LoadXML(XElement xElement)
	{
		base.LoadXML(xElement);
		SearchPattern = ExternalConnectionBase.GetChild(xElement, "SearchPattern")?.Value ?? "";
		string getChildExternalConnectionBaseTerm = ExternalConnectionBase.GetChild(xElement, "Recurse")?.Value ?? "";
		bool result;
		if (string.IsNullOrEmpty(getChildExternalConnectionBaseTerm))
		{
			Recurse = false;
		}
		else if (bool.TryParse(getChildExternalConnectionBaseTerm, out result))
		{
			Recurse = result;
		}
		else
		{
			Recurse = false;
		}
	}
}
