using System;
using System.Collections.Generic;
using Ras.Layers;
using Utility.Filtering;

namespace Ras.Process.PlanCommands;

internal sealed class PlanSelectorNode : ISelectorNode
{
	private readonly PlanFields _fields;

	public string Name => "plan";

	public ISelectorFields Fields => _fields;

	public IEnumerable<string> MemberNames => Array.Empty<string>();

	public PlanSelectorNode(Plan plan)
	{
		_fields = new PlanFields(plan);
	}

	public static PlanSelectorNode CreateSchema()
	{
		return new PlanSelectorNode(null);
	}

	public bool TryGetChildNode(string normalizedName, out ISelectorNode node)
	{
		node = null;
		return false;
	}

	public bool TryGetCollection(string normalizedName, out ISelectorCollection collection)
	{
		collection = null;
		return false;
	}
}
