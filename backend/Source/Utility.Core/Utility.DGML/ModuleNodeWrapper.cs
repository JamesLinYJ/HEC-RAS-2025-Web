using System.Diagnostics;
using System.Xml.Serialization;

namespace Utility.DGML;

public class ModuleNodeWrapper : Node
{
	[XmlAttribute]
	public string Company;

	[XmlAttribute]
	public string VersionInfo;

	[XmlAttribute]
	public string Filename;

	[XmlAttribute]
	public bool DebugMode;

	public ModuleNodeWrapper(ProcessModule mod)
		: base(mod.ModuleName, mod.ModuleName)
	{
		Filename = mod.FileName;
		VersionInfo = mod.FileVersionInfo.FileVersion;
		Company = mod.FileVersionInfo.CompanyName;
		DebugMode = mod.FileVersionInfo.IsDebug;
	}

	public ModuleNodeWrapper()
	{
	}
}
