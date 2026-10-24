using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Utility.Extensions;

namespace Utility.Processes;

[DebuggerDisplay("{ASMName}")]
public class AssemblyNode
{
	public Assembly Assembly { get; }

	public Exception Error { get; }

	public List<AssemblyNode> Dependencies { get; }

	public string ASMName { get; }

	public string FriendlyName
	{
		get
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(ASMName);
			if (Assembly == null)
			{
				fileNameWithoutExtension += " (Failed)";
			}
			return fileNameWithoutExtension;
		}
	}

	public FileVersionInfo VersionInfo => FileVersionInfo.GetVersionInfo(Assembly.Location);

	public AssemblyNode(string name, Assembly asm, Exception err = null)
	{
		ASMName = name;
		Assembly = asm;
		Error = err;
		Dependencies = new List<AssemblyNode>();
	}

	public List<AssemblyNode> FlattenDependencies()
	{
		List<AssemblyNode> assemblyNodes = new List<AssemblyNode>();
		if (Dependencies.IsNullOrEmpty())
		{
			return assemblyNodes;
		}
		foreach (AssemblyNode dependency in Dependencies)
		{
			assemblyNodes.Add(dependency);
			assemblyNodes.AddRange(dependency.FlattenDependencies());
		}
		return assemblyNodes;
	}

	public static AssemblyNode Load(string asmStr)
	{
		try
		{
			return new AssemblyNode(asmStr, Assembly.LoadFrom(asmStr));
		}
		catch (Exception err)
		{
			return new AssemblyNode(asmStr, null, err);
		}
	}

	public static AssemblyNode Load(AssemblyName asmName)
	{
		try
		{
			return new AssemblyNode(asmName.FullName, Assembly.Load(asmName));
		}
		catch
		{
			string asmNameTerm = asmName.Name;
			if (!asmNameTerm.EndsWith(".dll"))
			{
				asmNameTerm += ".dll";
			}
			return Load(asmNameTerm);
		}
	}
}
