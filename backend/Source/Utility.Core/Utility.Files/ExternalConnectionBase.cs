using System.Linq;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Files;

public abstract class ExternalConnectionBase
{
	protected static class Keys
	{
		public const string NameAttr = "Name";

		public const string SourceTag = "Source";

		public const string SourceTargetAttr = "Target";

		public const string SourceTypeAttr = "Type";

		public const string SourceSubTypeAttr = "SubType";

		public const string RelPathTag = "RelativePath";

		public const string AbsPathTag = "AbsolutePath";

		public const string DescTag = "Description";

		public const string DirectorySearchPattern = "SearchPattern";

		public const string DirectoryRecurse = "Recurse";
	}

	protected static readonly ILogger _logger = LogHelper.GetLogger("ExternalConnectionBase");

	public static readonly string XmlElementName = "Connection";

	protected abstract string Type { get; }

	public string Name { get; set; }

	public string Description { get; set; }

	public abstract OperationResult TryGetVersion(out int version);

	public abstract OperationResult TryGetHash(out string hash);

	public abstract OperationResult ResourceExists();

	public XElement SaveXML()
	{
		XElement xElement = new XElement(XmlElementName);
		SaveXML(xElement);
		return xElement;
	}

	public virtual void SaveXML(XElement xElement)
	{
		xElement.SetAttributeValue("Name", Name);
		xElement.SetAttributeValue("Type", Type);
		if (!string.IsNullOrEmpty(Description))
		{
			XElement xElement2 = new XElement("Description");
			xElement.Add(xElement2);
			xElement2.Value = Description;
		}
	}

	protected static XElement GetChild(XElement x, string key)
	{
		return x.Descendants().FirstOrDefault((XElement n) => n.Name.ToString() == key);
	}

	public static OperationResult TryLoadFromXML(string pathRelativeBase, XElement xElement, out ExternalConnectionBase connection)
	{
		XAttribute xAttribute = xElement.Attribute("Type");
		if (xAttribute == null || string.IsNullOrEmpty(xAttribute.Value))
		{
			connection = null;
			return OperationResult.Fail("No 'Type' attribute found on the ExternalConnection XML.");
		}
		string xAttributeTerm = xAttribute.Value;
		if (xAttributeTerm == "File")
		{
			connection = new FileExternalConnection(pathRelativeBase);
			connection.LoadXML(xElement);
			return true;
		}
		if (xAttributeTerm == "Directory")
		{
			connection = new DirectoryExternalConnection(pathRelativeBase);
			connection.LoadXML(xElement);
			return true;
		}
		_logger.LogWarning("Could not identify External Connection type {TypeName}.", xAttributeTerm);
		connection = null;
		return false;
	}

	public virtual void LoadXML(XElement xElement)
	{
		Description = xElement.Elements("Description").FirstOrDefault()?.Value ?? "";
		XAttribute xAttribute = xElement.Attribute("Name");
		XAttribute xAttribute2 = xElement.Attribute("Type");
		if (xAttribute != null)
		{
			Name = xAttribute.Value;
		}
		if (xAttribute2 != null)
		{
			string xAttribute2Term = xAttribute2.Value;
			if (xAttribute2Term != Type)
			{
				_logger.LogWarning("Type mismatch on XML loading for External Connection. Possible loading error. {Expected} -> {Actual}", Type, xAttribute2Term);
			}
		}
	}
}
