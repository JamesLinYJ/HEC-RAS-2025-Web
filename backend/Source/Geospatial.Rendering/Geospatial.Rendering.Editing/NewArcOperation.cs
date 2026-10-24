namespace Geospatial.Rendering.Editing;

public class NewArcOperation : NodeArcEditOperation
{
	public const int FallbackToHitTest = -1;

	private int _arcFID;

	private int _nodeFID;

	private bool _addedNode;

	private int _overrideStartNodeFID = -1;

	public override string Description => "Add Arc";

	public NewArcOperation(NodeArcEditContext context, HitTestArgs args, int overrideStartNodeFID = -1)
		: base(context, args)
	{
		_overrideStartNodeFID = overrideStartNodeFID;
	}

	protected override bool ApplyInternal()
	{
		int elementIndex;
		if (_overrideStartNodeFID != -1)
		{
			_nodeFID = _overrideStartNodeFID;
		}
		else if (Context.Network.HitTestNode(_location.QueryPoint, (double)Context.NodePixelHitDistance * _location.Window.CellSize, out elementIndex))
		{
			_nodeFID = elementIndex;
		}
		else
		{
			Context.Network.AddNode(_location.QueryPoint, out _nodeFID);
			Context.OnNodeAdded(_nodeFID);
			_addedNode = true;
		}
		Context.Network.AddArc(_nodeFID, -1, null, out _arcFID);
		Context.OnNewArcCreated(_arcFID);
		Context.OnArcAdded(_arcFID);
		SetArcSelection(_arcFID);
		return true;
	}

	protected override bool UndoInternal()
	{
		UndoSelectionState();
		Context.Network.RemoveArc(_arcFID);
		Context.OnArcRemoved(_arcFID);
		if (_addedNode)
		{
			Context.Network.RemoveNode(_nodeFID);
			Context.OnNodeRemoved(_nodeFID);
		}
		return true;
	}
}
