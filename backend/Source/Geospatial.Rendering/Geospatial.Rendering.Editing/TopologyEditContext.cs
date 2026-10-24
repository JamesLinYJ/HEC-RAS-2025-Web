using System;
using System.Collections.Generic;
using Geospatial.Rendering.Editing.Handlers.Topology;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class TopologyEditContext : NodeArcEditContext
{
	public Any<EditableTopology> AnyEditableTopology;

	public SelectedSet ControlPointSelectedSet;

	public EditableTopology EditableTopology => AnyEditableTopology;

	public Topology Topology => EditableTopology.Vector;

	public TopologyCompletionContext CompletionContext { get; internal set; }

	public override BaseTopology Network => Topology;

	public override IEditableNodeArcNetwork EditableNetwork => EditableTopology;

	public override int NodePixelHitDistance => 6;

	public override bool AllowOverlappingArcSplits => true;

	public event EditingSession.ElementsAddedEvent ControlPointsAdded;

	public event EditingSession.ElementsInsertedEvent ControlPointsInserted;

	public event EditingSession.ElementsRemovedEvent ControlPointsRemoved;

	public override void OnNewArcCreated(int arcFid)
	{
		Topology.Arcs[arcFid].ExternallyTracked_DateOfGenesis = DateTime.UtcNow.Ticks;
	}

	public override bool DebugArcHasCoincidentNodePoint(int arcFid)
	{
		return Topology.ArcHasInternalPointIdenticalToStartOrEndNode(arcFid);
	}

	public override EditOperation CreateTranslateSelectedNodesOperation(HitTestArgs hit, Vector delta)
	{
		return new TranslateSelectedNodesOperation(this, hit, delta);
	}

	public override DeleteArcPointsOperation CreateDeleteArcPointsOperation(int arcFid, Func<IPointCollection> getPointCollection, SelectedSet selectedPoints)
	{
		return new DeleteTopologyArcPointsOperation(this, arcFid, getPointCollection, selectedPoints);
	}

	public override void ClearSelection()
	{
		base.ClearSelection();
		ControlPointSelectedSet.Clear();
	}

	public override object CaptureSelectionExtraContext()
	{
		return new SelectedSet(ControlPointSelectedSet);
	}

	public override void RestoreSelectionExtraContext(object saved)
	{
		if (!(saved is SelectedSet selectedSet))
		{
			return;
		}
		ControlPointSelectedSet.Clear();
		foreach (int element in selectedSet.Elements)
		{
			ControlPointSelectedSet.Add(element);
		}
	}

	public override void ClearSelectionExtraContext()
	{
		ControlPointSelectedSet.Clear();
	}

	public override void Select(Extent ext, SelectMode mode)
	{
		TopologyRenderLayer.Select(Topology, ArcSelectedSet, NodeSelectedSet, ControlPointSelectedSet, ext, SelectionOptions.Get(LassoSelectionHandler.Options.HitMode), mode, SelectionOptions.Get(TopologySelectionOptions.Scope));
	}

	public override void Select(Polyline line, SelectMode mode)
	{
		TopologyRenderLayer.Select(Topology, ArcSelectedSet, NodeSelectedSet, ControlPointSelectedSet, line, mode, SelectionOptions.Get(TopologySelectionOptions.Scope));
	}

	public override void Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode)
	{
		TopologyRenderLayer.Select(Topology, ArcSelectedSet, NodeSelectedSet, ControlPointSelectedSet, lasso, hitMode, mode, SelectionOptions.Get(TopologySelectionOptions.Scope));
	}

	public void OnControlPointsAdded(int appendedCount)
	{
		ControlPointsAdded?.Invoke(appendedCount);
	}

	public void OnControlPointsInserted(IEnumerable<int> indices)
	{
		ControlPointsInserted?.Invoke(indices);
	}

	public void OnControlPointsRemoved(IEnumerable<int> indices)
	{
		ControlPointsRemoved?.Invoke(indices);
	}
}
