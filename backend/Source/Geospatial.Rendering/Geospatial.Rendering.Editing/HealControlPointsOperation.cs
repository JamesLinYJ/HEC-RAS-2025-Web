using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public class HealControlPointsOperation : TopologyEditOperation
{
	private Queue<Topology.HealCommand> _healCommands;

	public override string Description => "Heal Control Points";

	public HealControlPointsOperation(TopologyEditContext layer, Queue<Topology.HealCommand> healCommands)
		: base(layer, null)
	{
		_healCommands = new Queue<Topology.HealCommand>(healCommands);
	}

	protected override bool ApplyInternal()
	{
		foreach (Topology.HealCommand healCommand in _healCommands)
		{
			_context.Topology.UpdateControlPointPoint(healCommand.ControlPointIndex, healCommand.ToPoint);
			if (healCommand.ToRegionIndex != -1)
			{
				_context.Topology.CompletedRegions[healCommand.ToRegionIndex].TryAddControlPoint(healCommand.ControlPointIndex);
			}
			if (healCommand.FromRegionIndex != -1)
			{
				_context.Topology.CompletedRegions[healCommand.FromRegionIndex].RemoveControlPoint(healCommand.ControlPointIndex);
			}
		}
		return true;
	}

	protected override bool UndoInternal()
	{
		foreach (Topology.HealCommand reverse in _healCommands.Reverse())
		{
			_context.Topology.UpdateControlPointPoint(reverse.ControlPointIndex, reverse.FromPoint);
			if (reverse.ToRegionIndex != -1)
			{
				_context.Topology.CompletedRegions[reverse.ToRegionIndex].RemoveControlPoint(reverse.ControlPointIndex);
			}
			if (reverse.FromRegionIndex != -1)
			{
				_context.Topology.CompletedRegions[reverse.FromRegionIndex].TryAddControlPoint(reverse.ControlPointIndex);
			}
		}
		return true;
	}
}
