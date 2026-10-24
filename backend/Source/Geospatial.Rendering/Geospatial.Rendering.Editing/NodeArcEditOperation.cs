using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Geospatial.Rendering.Layers;

namespace Geospatial.Rendering.Editing;

public abstract class NodeArcEditOperation : EditOperation
{
	protected SelectedSet _previouslySelectedArcs;

	protected SelectedSet _previouslySelectedNodes;

	protected object _selectionContext;

	protected NodeArcEditContext Context { get; }

	protected NodeArcEditOperation(NodeArcEditContext context, [AllowNull] HitTestArgs args)
		: base(args)
	{
		Context = context;
		_location = args;
		_previouslySelectedArcs = new SelectedSet(context.ArcSelectedSet);
		_previouslySelectedNodes = new SelectedSet(context.NodeSelectedSet);
		_selectionContext = context.CaptureSelectionExtraContext();
	}

	protected void SetArcSelection(int fid)
	{
		Context.ClearSelectionExtraContext();
		Context.NodeSelectedSet.Clear();
		if (Context.ArcSelectedSet.Count != 1 || Context.ArcSelectedSet.First() != fid)
		{
			Context.ArcSelectedSet.Clear();
			Context.ArcSelectedSet.Add(fid);
		}
	}

	protected void SetNodeSelection(int fid)
	{
		Context.ClearSelectionExtraContext();
		Context.ArcSelectedSet.Clear();
		if (Context.NodeSelectedSet.Count != 1 || Context.NodeSelectedSet.First() != fid)
		{
			Context.NodeSelectedSet.Clear();
			Context.NodeSelectedSet.Add(fid);
		}
	}

	protected void ClearAllSelection()
	{
		Context.ArcSelectedSet.Clear();
		Context.NodeSelectedSet.Clear();
		Context.ClearSelectionExtraContext();
	}

	protected void UndoSelectionState()
	{
		RestoreSelectionState(Context.ArcSelectedSet, _previouslySelectedArcs);
		RestoreSelectionState(Context.NodeSelectedSet, _previouslySelectedNodes);
		Context.RestoreSelectionExtraContext(_selectionContext);
	}

	protected static void RestoreSelectionState(SelectedSet original, SelectedSet previous)
	{
		original.Clear();
		foreach (int element in previous.Elements)
		{
			original.Add(element);
		}
	}
}
