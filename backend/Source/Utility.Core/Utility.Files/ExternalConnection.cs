using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Utility.Extensions;
using Utility.Logging;

namespace Utility.Files;

public class ExternalConnection
{
	private static class Keys
	{
		public const string NameAttr = "Name";

		public const string SourceTag = "Source";

		public const string SourceTargetAttr = "Target";

		public const string SourceTypeAttr = "Type";

		public const string SourceSubTypeAttr = "SubType";

		public const string RelPathTag = "RelativePath";

		public const string AbsPathTag = "AbsolutePath";

		public const string DescTag = "Description";
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("ExternalConnection");

	public static readonly string XmlElementName = "Connection";

	private string _pathRelativeSourceDirectory;

	public bool AutoCorrectPathnamesOnSave { get; set; } = true;

	public string PathRelativeSourceDirectory => _pathRelativeSourceDirectory;

	public bool SupportsRelativePaths
	{
		get
		{
			if (string.IsNullOrEmpty(_pathRelativeSourceDirectory))
			{
				return false;
			}
			if (Path.IsPathRooted(AbsolutePath) && Path.GetPathRoot(AbsolutePath) != Path.GetPathRoot(PathRelativeSourceDirectory))
			{
				return false;
			}
			return true;
		}
	}

	public string RelativePath { get; private set; }

	public string AbsolutePath { get; private set; }

	public ExternalConnectionTarget TargetType { get; private set; }

	public string Name { get; set; }

	public string Description { get; set; }

	public ExternalConnectionType ConnectionType { get; set; }

	public string ConnectionSubTypeName { get; set; }

	public static ExternalConnectionType ParseConnectionType(string name)
	{
		if (name.RoughlyEquals("File"))
		{
			return ExternalConnectionType.FileSystem;
		}
		if (name.RoughlyEquals("EnvVar") || name.RoughlyEquals("EnvironmentVariable"))
		{
			return ExternalConnectionType.EnvironmentVariable;
		}
		if (name.RoughlyEquals("S3"))
		{
			return ExternalConnectionType.S3;
		}
		return ExternalConnectionType.FileSystem;
	}

	public static ExternalConnectionTarget ParseConnectionTarget(string name)
	{
		if (name.RoughlyEquals("File"))
		{
			return ExternalConnectionTarget.File;
		}
		if (name.RoughlyEquals("Directory"))
		{
			return ExternalConnectionTarget.Directory;
		}
		return ExternalConnectionTarget.File;
	}

	public static string Serialize(ExternalConnectionType connType)
	{
		return connType switch
		{
			ExternalConnectionType.FileSystem => "FileSystem", 
			ExternalConnectionType.EnvironmentVariable => "EnvironmentVariable", 
			ExternalConnectionType.S3 => "S3", 
			_ => "FileSystem", 
		};
	}

	public static string Serialize(ExternalConnectionTarget connTarget)
	{
		return connTarget switch
		{
			ExternalConnectionTarget.File => "File", 
			ExternalConnectionTarget.Directory => "Directory", 
			_ => "File", 
		};
	}

	public static bool TryReconcile(ref string absPath, ref string relPath, string rootRelativeDir)
	{
		return TryReconcile(ref absPath, ref relPath, rootRelativeDir, FileHelpers.CanReadFile);
	}

	public static bool TryReconcile(ref string absPath, ref string relPath, string rootRelativeDir, Func<string, bool> targetExists)
	{
		try
		{
			bool notIsNullOrEmpty = !string.IsNullOrEmpty(rootRelativeDir);
			if ((!string.IsNullOrEmpty(relPath) && !Path.IsPathFullyQualified(relPath)) & notIsNullOrEmpty)
			{
				string absPath2 = FileHelpers.MakeAbsolute(rootRelativeDir, relPath);
				if (targetExists(absPath2))
				{
					absPath = absPath2;
					return true;
				}
			}
			if (targetExists(absPath))
			{
				if (notIsNullOrEmpty)
				{
					relPath = CureRelative(rootRelativeDir, absPath);
				}
				return true;
			}
			return false;
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Failed to reconcile paths. Abs: \"{AbsPath}\" Rel: \"{RelPath}\"", absPath, relPath);
			return false;
		}
	}

	private static string CureRelative(string rootRelativeDir, string absolutePath)
	{
		if (string.IsNullOrEmpty(rootRelativeDir) || !Path.IsPathRooted(absolutePath))
		{
			return "";
		}
		string result = FileHelpers.MakeRelative(rootRelativeDir, absolutePath, out var isRelative);
		if (!isRelative)
		{
			return "";
		}
		return result;
	}

	public ExternalConnection(string pathRelativeDirectory)
	{
		_pathRelativeSourceDirectory = pathRelativeDirectory;
		ConnectionType = ExternalConnectionType.FileSystem;
		TargetType = ExternalConnectionTarget.File;
	}

	public static ExternalConnection CreateFileConnection(string name, string connectingFilename, string pathRelativeDirectory)
	{
		ExternalConnection externalConnection = new ExternalConnection(pathRelativeDirectory);
		externalConnection.TargetType = ExternalConnectionTarget.File;
		externalConnection.ConnectionType = ExternalConnectionType.FileSystem;
		externalConnection.Name = name;
		externalConnection.SetConnectedPathname(connectingFilename);
		return externalConnection;
	}

	public static ExternalConnection CreateAbsoluteFileConnection(string name, string connectingFilename)
	{
		ExternalConnection externalConnection = new ExternalConnection("");
		externalConnection.TargetType = ExternalConnectionTarget.File;
		externalConnection.ConnectionType = ExternalConnectionType.FileSystem;
		externalConnection.Name = name;
		externalConnection.SetConnectedPathname(connectingFilename);
		return externalConnection;
	}

	public void SetConnectedPathname(string pathname)
	{
		if (!Path.IsPathFullyQualified(pathname))
		{
			throw new ArgumentException(pathname.InDoubleQuotes() + " is not a fully-qualified path.");
		}
		if (SupportsRelativePaths)
		{
			string relativePath = FileHelpers.MakeRelative(_pathRelativeSourceDirectory, pathname, out var isRelative);
			if (isRelative)
			{
				RelativePath = relativePath;
			}
			else
			{
				RelativePath = "";
			}
		}
		else
		{
			RelativePath = "";
		}
		AbsolutePath = pathname;
	}

	public bool TryGetExistingPathname(out string pathname)
	{
		pathname = "";
		bool notIsNullOrEmptyAndSupportsRelativePaths = !string.IsNullOrEmpty(RelativePath) && SupportsRelativePaths;
		bool notIsNullOrEmpty = !string.IsNullOrEmpty(AbsolutePath);
		if (ConnectionType == ExternalConnectionType.FileSystem)
		{
			if (!notIsNullOrEmptyAndSupportsRelativePaths && !notIsNullOrEmpty)
			{
				pathname = "";
				return false;
			}
			if (notIsNullOrEmptyAndSupportsRelativePaths)
			{
				pathname = FileHelpers.MakeAbsolute(_pathRelativeSourceDirectory, RelativePath);
				if (FSExistsAs(pathname))
				{
					return true;
				}
			}
			if (notIsNullOrEmpty)
			{
				if (FSExistsAs(AbsolutePath))
				{
					pathname = AbsolutePath;
					return true;
				}
				if (!notIsNullOrEmptyAndSupportsRelativePaths)
				{
					pathname = AbsolutePath;
					return false;
				}
			}
			return false;
		}
		throw new NotImplementedException();
	}

	private bool FSExistsAs(string path)
	{
		if (TargetType == ExternalConnectionTarget.File)
		{
			return FileHelpers.CanReadFile(path);
		}
		if (TargetType == ExternalConnectionTarget.Directory)
		{
			return Directory.Exists(path);
		}
		throw new NotSupportedException();
	}

	public XElement SaveXML()
	{
		XElement xElement = new XElement(XmlElementName);
		xElement.SetAttributeValue("Name", Name);
		XElement srcX = new XElement("Source");
		xElement.Add(srcX);
		srcX.SetAttributeValue("Target", Serialize(TargetType));
		srcX.SetAttributeValue("Type", Serialize(ConnectionType));
		if (!string.IsNullOrEmpty(ConnectionSubTypeName))
		{
			srcX.SetAttributeValue("SubType", ConnectionSubTypeName);
		}
		Action action = () =>
		{
			if (SupportsRelativePaths && !string.IsNullOrEmpty(RelativePath))
			{
				XElement xElement5 = new XElement("RelativePath");
				srcX.Add(xElement5);
				xElement5.Value = RelativePath;
			}
			if (!string.IsNullOrEmpty(AbsolutePath))
			{
				XElement xElement6 = new XElement("AbsolutePath");
				srcX.Add(xElement6);
				xElement6.Value = AbsolutePath;
			}
		};
		if (AutoCorrectPathnamesOnSave && ConnectionType == ExternalConnectionType.FileSystem)
		{
			if (TryGetExistingPathname(out var pathname))
			{
				if (SupportsRelativePaths)
				{
					XElement xElement2 = new XElement("RelativePath");
					srcX.Add(xElement2);
					xElement2.Value = FileHelpers.MakeRelative(_pathRelativeSourceDirectory, pathname);
				}
				XElement xElement3 = new XElement("AbsolutePath");
				srcX.Add(xElement3);
				xElement3.Value = pathname;
			}
			else
			{
				action();
			}
		}
		else
		{
			action();
		}
		if (!string.IsNullOrEmpty(Description))
		{
			XElement xElement4 = new XElement("Description");
			xElement.Add(xElement4);
			xElement4.Value = Description;
		}
		return xElement;
	}

	public void LoadXML(XElement rootX)
	{
		Name = rootX.Attribute("Name")?.Value ?? "";
		XElement xElement = GetChild(rootX, "Source");
		if (xElement != null)
		{
			TargetType = ParseConnectionTarget(xElement.Attribute("Target")?.Value ?? "");
			ConnectionType = ParseConnectionType(xElement.Attribute("Type")?.Value ?? "");
			ConnectionSubTypeName = xElement.Attribute("SubType")?.Value ?? "";
			RelativePath = GetChild(xElement, "RelativePath")?.Value ?? "";
			AbsolutePath = GetChild(xElement, "AbsolutePath")?.Value ?? "";
		}
		Description = GetChild(rootX, "Description")?.Value ?? "";
		static XElement GetChild(XElement x, string key)
		{
			return x.Descendants().FirstOrDefault((XElement n) => n.Name.ToString().RoughlyEquals(key));
		}
	}
}
