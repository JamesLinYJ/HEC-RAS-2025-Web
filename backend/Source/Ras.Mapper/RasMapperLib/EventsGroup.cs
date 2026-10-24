using System.Collections.Generic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class EventsGroup : BaseGroup
{
	public override string NodeName => "Event Conditions";

	public override string NodeText => NodeName;

	public override string XMLNodeName => "EventConditions";

	public RASEventConditions FindEvent(string hdfFilename)
	{
		foreach (TreeNode node in Nodes)
		{
			if (node is RASEventConditions rASEventConditions && Operators.CompareString(rASEventConditions.SourceFilename, "", TextCompare: false) != 0 && Operators.CompareString(rASEventConditions.SourceFilename.ToLower(), hdfFilename.ToLower(), TextCompare: false) == 0)
			{
				return rASEventConditions;
			}
		}
		return null;
	}

	public List<RASEventConditions> FindAllEvents()
	{
		List<RASEventConditions> rasEventConditions = new List<RASEventConditions>();
		FindEvents(Nodes, rasEventConditions);
		return rasEventConditions;
	}

	private void FindEvents(TreeNodeCollection treeNodes, List<RASEventConditions> events)
	{
		foreach (TreeNode treeNode in treeNodes)
		{
			if (treeNode is RASGeometry)
			{
				events.Add((RASEventConditions)treeNode);
			}
			if (treeNode.Nodes.Count > 0)
			{
				FindEvents(treeNode.Nodes, events);
			}
		}
	}

	public RASEventConditions EnsureEvent(string title, string hdfFilename)
	{
		RASEventConditions rASEventConditions = FindEvent(hdfFilename);
		if (rASEventConditions == null)
		{
			rASEventConditions = new RASEventConditions(hdfFilename);
			rASEventConditions.SetDisplayName(title);
			Nodes.Add(rASEventConditions);
		}
		else if (Operators.CompareString(title, rASEventConditions.Name, TextCompare: false) != 0)
		{
			rASEventConditions.Name = title;
			rASEventConditions.Text = title;
		}
		return rASEventConditions;
	}

	public RASEventConditions EnsureEvent(string hdfFilename)
	{
		RASEventConditions rASEventConditions = FindEvent(hdfFilename);
		if (rASEventConditions == null)
		{
			rASEventConditions = new RASEventConditions(hdfFilename);
			Nodes.Add(rASEventConditions);
		}
		return rASEventConditions;
	}
}
