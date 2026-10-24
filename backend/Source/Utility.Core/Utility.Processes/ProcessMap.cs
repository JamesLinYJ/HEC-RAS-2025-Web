using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Utility.DGML;
using Utility.Extensions;
using Utility.Files;

namespace Utility.Processes;

public class ProcessMap
{
	public AssemblyMap NetAssemblyMap;

	public VCRuntimeMap VCRuntimeMap;

	public ModuleMap LoadedModules;

	public ProcessMap(string fromDll)
	{
		using (new SetCurrentDirectory(Path.GetDirectoryName(fromDll)))
		{
			NetAssemblyMap = AssemblyLoader.TryLoadAssemblyRecursive(Path.GetFileName(fromDll));
		}
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			VCRuntimeMap = new VCRuntimeMap();
		}
		LoadedModules = new ModuleMap();
	}

	public bool HasErrors(out Exception ex)
	{
		AssemblyNode assemblyNode = NetAssemblyMap.Nodes.FirstOrDefault();
		if (assemblyNode.Error != null)
		{
			ex = assemblyNode.Error;
			return true;
		}
		ex = null;
		return false;
	}

	public void ExportDGML(string xmlFilename, bool removeSystemNodes = true)
	{
		LoadedModules.Refresh();
		DGMLWriter dGMLWriter = new DGMLWriter();
		NetAssemblyMap.ExportDGML(dGMLWriter);
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			VCRuntimeMap.ExportDGML(dGMLWriter);
		}
		LoadedModules.ExportDGML(dGMLWriter);
		dGMLWriter.Serialize(xmlFilename);
	}

	public bool TryCallStaticFunction(string assemblyName, string className, string functionName, object[] parameters, out string errorMessage)
	{
		try
		{
			AssemblyNode assemblyNode = NetAssemblyMap.Nodes.FirstOrDefault((AssemblyNode asm) => asm.Assembly.GetName().Name.RoughlyEquals(assemblyName));
			if (assemblyNode == null)
			{
				errorMessage = "Could not load assembly " + assemblyName;
				return false;
			}
			Type type = assemblyNode.Assembly.ExportedTypes.FirstOrDefault((Type et) => et.Name.RoughlyEquals(className));
			if (type == null)
			{
				errorMessage = "Could not load class " + className + " in assembly " + assemblyName;
				return false;
			}
			Type[] typeValues = new Type[parameters.Length];
			for (int intervalIndex = 0; intervalIndex < parameters.Length; intervalIndex++)
			{
				typeValues[intervalIndex] = parameters[intervalIndex].GetType();
			}
			MethodInfo method = type.GetMethod(functionName, typeValues);
			if (method == null)
			{
				errorMessage = $"Could not load {functionName} from class {className} in assembly {assemblyName}";
				return false;
			}
			method.Invoke(null, parameters);
		}
		catch (Exception ex)
		{
			errorMessage = $"Error calling {functionName} from class {className} in assembly {assemblyName}" + Environment.NewLine + ex.Message;
			return false;
		}
		errorMessage = "";
		return true;
	}
}
