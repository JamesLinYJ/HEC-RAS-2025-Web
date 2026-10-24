using System;
using System.Diagnostics;

namespace Visual.Mapping.Observables;

[DebuggerDisplay("Leaf: {Tool?.DisplayName,nq} ({Tool?.Id,nq}) Active={IsActive}")]
public sealed class ToolLeaf : ToolNode
{
	public ToolDescriptor Tool { get; init; }

	public override ToolDescriptor FaceDescriptor => Tool;

	public ToolLeaf(ToolDescriptor tool, Func<bool> availableWhen = null)
	{
		Tool = tool;
		AvailableWhen = availableWhen;
	}

	public override bool ContainsDescriptor(ToolDescriptor descriptor)
	{
		if (descriptor != null)
		{
			return Tool == descriptor;
		}
		return false;
	}
}
