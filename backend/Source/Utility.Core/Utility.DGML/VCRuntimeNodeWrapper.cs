using System.Runtime.Versioning;
using System.Xml.Serialization;
using Utility.Processes;

namespace Utility.DGML;

[SupportedOSPlatform("windows")]
public class VCRuntimeNodeWrapper : Node
{
	[XmlAttribute]
	public string VersionInfo;

	public VCRuntimeNodeWrapper(VCRuntime vcr)
		: base(vcr.RegistryValue, vcr.FriendlyName)
	{
		VersionInfo = vcr.VersionString;
	}

	public VCRuntimeNodeWrapper()
	{
	}
}
