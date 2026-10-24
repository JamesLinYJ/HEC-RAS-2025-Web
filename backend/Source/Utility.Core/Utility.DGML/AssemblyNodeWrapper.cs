using System.Xml.Serialization;
using Utility.Processes;

namespace Utility.DGML;

public class AssemblyNodeWrapper : Node
{
	[XmlAttribute]
	public string Company;

	[XmlAttribute]
	public string Filename;

	[XmlAttribute]
	public bool IsDebugMode;

	public AssemblyNodeWrapper(AssemblyNode asmn)
		: base(asmn.ASMName, asmn.FriendlyName)
	{
		Company = asmn.VersionInfo.CompanyName;
		Filename = asmn.Assembly.Location;
		IsDebugMode = asmn.VersionInfo.IsDebug;
	}

	public AssemblyNodeWrapper()
	{
	}
}
