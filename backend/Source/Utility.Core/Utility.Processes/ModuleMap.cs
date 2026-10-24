using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Utility.DGML;
using Utility.Files;

namespace Utility.Processes;

public class ModuleMap
{
	public List<ProcessModule> LoadedModules;

	public ModuleMap()
	{
		Refresh();
	}

	public void Refresh()
	{
		LoadedModules = new List<ProcessModule>();
		foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
		{
			LoadedModules.Add(module);
		}
	}

	public void ExportXML(string xmlFilename)
	{
		if (File.Exists(xmlFilename))
		{
			File.Delete(xmlFilename);
		}
		SerializeXML().Save(xmlFilename);
	}

	public XElement SerializeXML(bool skipMsftLibs = false)
	{
		XElement xElement = new XElement("Modules");
		for (int i = 0; i < LoadedModules.Count; i++)
		{
			ProcessModule processModule = LoadedModules[i];
			if (!skipMsftLibs || !AssemblyMap.IsMicrosoftLibrary(processModule.FileVersionInfo))
			{
				xElement.Add(BuildModuleXElement(processModule));
			}
		}
		return xElement;
	}

	public static XElement BuildModuleXElement(ProcessModule mod)
	{
		XElement xElement = new XElement("Module");
		xElement.Add(new XElement("Filename")
		{
			Value = mod.FileName
		});
		xElement.Add(new XElement("Version")
		{
			Value = (mod.FileVersionInfo?.FileVersion ?? "")
		});
		xElement.Add(new XElement("SHA256")
		{
			Value = FileHelpers.ComputeFileSHA256(mod.FileName)
		});
		return xElement;
	}

	public void ExportDGML(string xmlFilename)
	{
		DGMLWriter dGMLWriter = new DGMLWriter();
		ExportDGML(dGMLWriter);
		dGMLWriter.Serialize(xmlFilename);
	}

	public void ExportSystemDGML(DGMLWriter writer)
	{
		for (int i = 0; i < LoadedModules.Count; i++)
		{
			ProcessModule processModule = LoadedModules[i];
			if (AssemblyMap.IsMicrosoftLibrary(processModule.FileVersionInfo))
			{
				writer.AddNode(new ModuleNodeWrapper(processModule));
			}
		}
	}

	public void ExportDGML(DGMLWriter writer)
	{
		ContainerNode containerNode = writer.AddUniqueContainerNode("Native Assemblies (Loaded)");
		ContainerNode containerNode2 = writer.AddUniqueContainerNode("Microsoft Native Assemblies");
		writer.AddToGroup(containerNode, containerNode2);
		List<ModuleNodeWrapper> moduleNodeWrappers = new List<ModuleNodeWrapper>();
		int ordinal = 0;
		for (int i = 0; i < LoadedModules.Count; i++)
		{
			ProcessModule processModule = LoadedModules[i];
			ModuleNodeWrapper moduleNodeWrapper = new ModuleNodeWrapper(processModule);
			if (moduleNodeWrapper.DebugMode)
			{
				ordinal++;
			}
			writer.AddNode(moduleNodeWrapper);
			if (AssemblyMap.IsMicrosoftLibrary(processModule.FileVersionInfo))
			{
				moduleNodeWrappers.Add(moduleNodeWrapper);
			}
			else
			{
				writer.AddToGroup(containerNode, moduleNodeWrapper);
			}
		}
		if (ordinal > 0)
		{
			containerNode.Label += $" ({ordinal}/{LoadedModules.Count} Debug mode)";
		}
		else
		{
			containerNode.Label += " (All Release Mode)";
		}
		foreach (IGrouping<string, ModuleNodeWrapper> iGrouping in from node in moduleNodeWrappers
			group node by node.VersionInfo)
		{
			if (iGrouping.Count() > 1)
			{
				writer.AddToGroup(containerNode2, writer.AddGroupedNodes(iGrouping.ToList(), iGrouping.Key));
				continue;
			}
			ModuleNodeWrapper moduleNodeWrapper2 = iGrouping.FirstOrDefault();
			writer.AddNode(moduleNodeWrapper2);
			writer.AddToGroup(containerNode2, moduleNodeWrapper2);
		}
	}

	public void ExportDGML_prev(DGMLWriter writer)
	{
		List<ModuleNodeWrapper> moduleNodeWrappers = new List<ModuleNodeWrapper>();
		for (int i = 0; i < LoadedModules.Count; i++)
		{
			ProcessModule processModule = LoadedModules[i];
			ModuleNodeWrapper moduleNodeWrapper = new ModuleNodeWrapper(processModule);
			if (AssemblyMap.IsMicrosoftLibrary(processModule.FileVersionInfo))
			{
				moduleNodeWrappers.Add(moduleNodeWrapper);
			}
			else
			{
				writer.AddNode(moduleNodeWrapper);
			}
		}
		int moduleNodeWrappersModuleNodeWrapperTerm = moduleNodeWrappers.Count((ModuleNodeWrapper mm) => mm.DebugMode);
		int count = moduleNodeWrappers.Count;
		string countTerm = "Microsoft Native Assemblies (Loaded)";
		countTerm = ((moduleNodeWrappersModuleNodeWrapperTerm <= 0) ? (countTerm + " (All Release Mode)") : (countTerm + $" ({moduleNodeWrappersModuleNodeWrapperTerm}/{count} Debug mode)"));
		IEnumerable<IGrouping<string, ModuleNodeWrapper>> enumerable = from node in moduleNodeWrappers
			group node by node.VersionInfo;
		ContainerNode owner = writer.AddUniqueContainerNode(countTerm);
		foreach (IGrouping<string, ModuleNodeWrapper> enumerable2 in enumerable)
		{
			if (enumerable2.Count() > 1)
			{
				writer.AddToGroup(owner, writer.AddGroupedNodes(enumerable2.ToList(), enumerable2.Key));
				continue;
			}
			ModuleNodeWrapper moduleNodeWrapper2 = enumerable2.FirstOrDefault();
			writer.AddNode(moduleNodeWrapper2);
			writer.AddToGroup(owner, moduleNodeWrapper2);
		}
	}
}
