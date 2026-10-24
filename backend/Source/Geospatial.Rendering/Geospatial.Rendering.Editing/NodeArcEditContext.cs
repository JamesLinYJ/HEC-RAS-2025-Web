using System;
using Geospatial.Rendering.Editing.Options;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Geospatial.Rendering.Editing;

public abstract class NodeArcEditContext
{
	public SelectedSet ArcSelectedSet;

	public SelectedSet NodeSelectedSet;

	public SelectedSet OpenFeatureSelectedSetCache { get; } = new SelectedSet();

	public OptionStore SelectionOptions { get; set; } = OptionStore.Empty;

	public abstract BaseTopology Network { get; }

	public abstract IEditableNodeArcNetwork EditableNetwork { get; }

	public abstract int NodePixelHitDistance { get; }

	public virtual bool AllowOverlappingArcSplits => false;

	public event EditingSession.ElementAddedEvent ArcAdded;

	public event EditingSession.ElementInsertedEvent ArcInserted;

	public event EditingSession.ElementRemovedEvent ArcRemoved;

	public event EditingSession.ElementAddedEvent NodeAdded;

	public event EditingSession.ElementInsertedEvent NodeInserted;

	public event EditingSession.ElementRemovedEvent NodeRemoved;

	public virtual void ClearSelection()
	{
		ArcSelectedSet.Clear();
		NodeSelectedSet.Clear();
	}

	public abstract EditOperation CreateTranslateSelectedNodesOperation(HitTestArgs hit, Vector delta);

	public abstract DeleteArcPointsOperation CreateDeleteArcPointsOperation(int arcFid, Func<IPointCollection> getPointCollection, SelectedSet selectedPoints);

	public virtual void OnNewArcCreated(int arcFid)
	{
	}

	public virtual bool DebugArcHasCoincidentNodePoint(int arcFid)
	{
		return false;
	}

	public virtual object CaptureSelectionExtraContext()
	{
		return null;
	}

	public virtual void RestoreSelectionExtraContext(object saved)
	{
	}

	public virtual void ClearSelectionExtraContext()
	{
	}

	public abstract void Select(Extent ext, SelectMode mode);

	public abstract void Select(Polyline line, SelectMode mode);

	public abstract void Select(Polygon lasso, MarqueeHitMode hitMode, SelectMode mode);

	public void OnNodeAdded(int idx)
	{
		NodeAdded?.Invoke(idx);
	}

	public void OnNodeInserted(int idx)
	{
		NodeInserted?.Invoke(idx);
	}

	public void OnNodeRemoved(int idx)
	{
		NodeRemoved?.Invoke(idx);
	}

	public void OnArcAdded(int idx)
	{
		ArcAdded?.Invoke(idx);
	}

	public void OnArcInserted(int idx)
	{
		ArcInserted?.Invoke(idx);
	}

	public void OnArcRemoved(int idx)
	{
		ArcRemoved?.Invoke(idx);
	}
}
