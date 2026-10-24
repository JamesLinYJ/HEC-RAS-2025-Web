using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using Utility.DGML;

namespace Utility.Processes;

[SupportedOSPlatform("windows")]
public class VCRuntimeMap
{
	public List<VCRuntime> Runtimes { get; }

	public VCRuntimeMap(IEnumerable<VCRuntime> runtimes)
	{
		Runtimes = runtimes.ToList();
	}

	public VCRuntimeMap()
	{
		Runtimes = VCRuntime.ReadCppRegistryKeys();
	}

	public void ExportDGML(string xmlFilename)
	{
		DGMLWriter dGMLWriter = new DGMLWriter();
		ExportDGML(dGMLWriter);
		dGMLWriter.Serialize(xmlFilename);
	}

	public void ExportDGML(DGMLWriter writer)
	{
		ContainerNode addToMegaGroup = writer.AddUniqueContainerNode("Visual C Runtimes (Installed)");
		List<VCRuntime> curRuntimes = Runtimes.ToList();
		AddNodesCleanListIfContains(curRuntimes, "2005", writer, addToMegaGroup, "2005");
		AddNodesCleanListIfContains(curRuntimes, "2008", writer, addToMegaGroup, "2008");
		AddNodesCleanListIfContains(curRuntimes, "2010", writer, addToMegaGroup, "2010");
		AddNodesCleanListIfContains(curRuntimes, "2012", writer, addToMegaGroup, "2012");
		AddNodesCleanListIfContains(curRuntimes, "2013", writer, addToMegaGroup, "2013");
		AddNodesCleanListIfContains(curRuntimes, "2015", writer, addToMegaGroup, "2015");
		AddNodesCleanListIfContains(curRuntimes, "2017", writer, addToMegaGroup, "2017");
		AddNodesCleanListIfContains(curRuntimes, "", writer, addToMegaGroup, "Other");
	}

	private void AddNodesCleanListIfContains(List<VCRuntime> curRuntimes, string textValue, DGMLWriter writer, ContainerNode addToMegaGroup, string groupName)
	{
		IEnumerable<VCRuntime> containsX;
		if (string.IsNullOrEmpty(textValue))
		{
			containsX = curRuntimes.ToList();
		}
		else
		{
			containsX = curRuntimes.Where((VCRuntime r) => r.RegistryValue.Contains(textValue));
		}
		if (containsX.Count() > 0)
		{
			ContainerNode subNode = writer.AddGroupedNodes(containsX.Select((VCRuntime vcr) => new VCRuntimeNodeWrapper(vcr)), groupName);
			curRuntimes.RemoveAll((VCRuntime aa) => containsX.Contains(aa));
			writer.AddToGroup(addToMegaGroup, subNode);
		}
	}
}
