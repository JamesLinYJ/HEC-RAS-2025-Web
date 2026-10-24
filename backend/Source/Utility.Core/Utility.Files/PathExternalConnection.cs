using System.Linq;
using System.Xml.Linq;

namespace Utility.Files;

public abstract class PathExternalConnection : ExternalConnectionBase
{
	public string RelativePath { get; set; }

	public string AbsolutePath { get; set; }

	public string PathRelativeSourceDirectory { get; private set; }

	protected PathExternalConnection(string relativeToPath)
	{
		PathRelativeSourceDirectory = relativeToPath;
	}

	public void SetConnectedPathname(string pathname)
	{
		AbsolutePath = pathname;
		RelativePath = (string.IsNullOrEmpty(PathRelativeSourceDirectory) ? "" : FileHelpers.MakeRelative(PathRelativeSourceDirectory, pathname));
	}

	public abstract void TryReconcilePaths();

	public override void SaveXML(XElement rootXElement)
	{
		base.SaveXML(rootXElement);
		if (!string.IsNullOrEmpty(RelativePath))
		{
			XElement xElement = new XElement("RelativePath");
			rootXElement.Add(xElement);
			xElement.Value = RelativePath;
		}
		if (!string.IsNullOrEmpty(AbsolutePath))
		{
			XElement xElement2 = new XElement("AbsolutePath");
			rootXElement.Add(xElement2);
			xElement2.Value = AbsolutePath;
		}
	}

	public override void LoadXML(XElement xElement)
	{
		base.LoadXML(xElement);
		XElement xElement2 = xElement.Elements("RelativePath").FirstOrDefault();
		if (xElement2 != null)
		{
			RelativePath = xElement2.Value;
		}
		XElement xElement3 = xElement.Elements("AbsolutePath").FirstOrDefault();
		if (xElement3 != null)
		{
			AbsolutePath = xElement3.Value;
		}
	}
}
