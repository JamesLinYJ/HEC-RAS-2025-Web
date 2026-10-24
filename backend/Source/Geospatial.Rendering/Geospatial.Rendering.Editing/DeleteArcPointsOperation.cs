using System;
using Geospatial.Rendering.Layers;
using Geospatial.Vectors;

namespace Geospatial.Rendering.Editing;

public abstract class DeleteArcPointsOperation : DeletePointOperation
{
	protected readonly int _arcFid;

	private readonly NodeArcEditContext _context;

	public override string Description => "Delete Arc Points";

	public bool ArcWasDeleted { get; private set; }

	protected DeleteArcPointsOperation(NodeArcEditContext context, int arcFid, Func<IPointCollection> getPointCollection, SelectedSet selectedPoints)
		: base(getPointCollection, selectedPoints)
	{
		_context = context;
		_arcFid = arcFid;
	}

	protected abstract void DeleteArc();

	protected abstract void RestoreArc();

	protected override bool ApplyInternal()
	{
		ArcWasDeleted = false;
		if (!base.ApplyInternal())
		{
			return false;
		}
		if (!NodeArcEditGeometry.IsCollapsedLoopArc(_context.Network, _arcFid))
		{
			return true;
		}
		DeleteArc();
		_selectedSet.Clear();
		ArcWasDeleted = true;
		return true;
	}

	protected override bool UndoInternal()
	{
		if (ArcWasDeleted)
		{
			RestoreArc();
			ArcWasDeleted = false;
		}
		return base.UndoInternal();
	}
}
