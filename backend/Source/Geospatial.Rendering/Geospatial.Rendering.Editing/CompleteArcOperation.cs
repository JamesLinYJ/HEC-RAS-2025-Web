using System;
using System.Collections.Generic;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class CompleteArcOperation : NodeArcEditOperation
{
	private enum CompleteOperation
	{
		ExistingNode,
		NewNode,
		SplitArc
	}

	private int _createdNodeFID = -1;

	private CompactList<SplitArcOperation.SplitArcResult> _splitArcs;

	private bool _replacedLastInternalPoint;

	private Point _lastInternalPointLoc;

	private CompleteOperation _completeOperation;

	private int _arcIndex;

	public override string Description => "Complete arc";

	public bool CreatedNode => _createdNodeFID != -1;

	public int CreatedNodeFID => _createdNodeFID;

	public CompleteArcOperation(NodeArcEditContext context, int arcIndex, HitTestArgs args = null)
		: base(context, args)
	{
		_arcIndex = arcIndex;
	}

	protected override bool ApplyInternal()
	{
		BaseTopology network = Context.Network;
		if (_location == null)
		{
			int internalPointCount = network.Arcs[_arcIndex].InternalPointCount;
			Point point = network.Arcs[_arcIndex].InternalPoints[internalPointCount - 1];
			TryRemoveCoincidentInternalPoint(point);
			ApplyConnectToNewNode(point);
		}
		else
		{
			Point queryPoint = _location.QueryPoint;
			TryRemoveCoincidentInternalPoint(queryPoint);
			double cellSize = _location.Window.CellSize;
			bool hitTestNode = network.HitTestNode(queryPoint, 6.0 * cellSize, out var elementIndex);
			bool hitTestArcs = network.HitTestArcs(queryPoint, 5.0 * cellSize, 5.0 * cellSize, out var _, out var hitElements);
			if (hitTestNode)
			{
				if (!NodeArcEditGeometry.CanFinishAddingAtNode(network, _arcIndex, elementIndex))
				{
					throw new InvalidOperationException("Completed arc on invalid node.");
				}
				ApplyConnectToExistingNode(elementIndex);
			}
			else if (hitTestArcs)
			{
				if (!ApplyConnectToSplitArc(_location, hitElements))
				{
					int fid = hitElements[0].Fid;
					if (network.GetArcPolyline(fid).NearestPointOnPolyline(_location.QueryPoint, hitElements[0].Ranges, out var _).SegmentIndex == 0)
					{
						ApplyConnectToExistingNode(network.Arcs[fid].StartNode);
					}
					else
					{
						ApplyConnectToExistingNode(network.Arcs[fid].EndNode);
					}
				}
			}
			else
			{
				ApplyConnectToNewNode(queryPoint);
			}
		}
		return true;
	}

	private void TryRemoveCoincidentInternalPoint(Point nodePt)
	{
		BaseTopology network = Context.Network;
		int internalPointCount = network.Arcs[_arcIndex].InternalPointCount;
		if (internalPointCount != 0)
		{
			Point lastInternalPointLoc = network.Arcs[_arcIndex].InternalPoints[internalPointCount - 1];
			double scaledCellSize = ((_location == null) ? 1.401298464324817E-45 : (10.0 * _location.Window.CellSize));
			if (!(lastInternalPointLoc.DistanceTo(nodePt) > scaledCellSize))
			{
				network.Arcs[_arcIndex].InternalPoints.RemoveAt(internalPointCount - 1);
				_replacedLastInternalPoint = true;
				_lastInternalPointLoc = lastInternalPointLoc;
			}
		}
	}

	private void TryReturnCoincidentInternalPoint()
	{
		if (_replacedLastInternalPoint)
		{
			Context.Network.Arcs[_arcIndex].AddPoint(_lastInternalPointLoc);
		}
	}

	private void ApplyConnectToExistingNode(int nodeId)
	{
		_completeOperation = CompleteOperation.ExistingNode;
		Context.Network.ReconnectArc(_arcIndex, nodeId, isStart: false);
	}

	private void ApplyConnectToNewNode(Point pt)
	{
		_completeOperation = CompleteOperation.NewNode;
		Context.Network.AddNode(pt, out _createdNodeFID);
		Context.OnNodeAdded(_createdNodeFID);
		Context.Network.ReconnectArc(_arcIndex, _createdNodeFID, isStart: false);
	}

	private void UndoConnectToNewNode()
	{
		Context.Network.RemoveNode(_createdNodeFID);
		Context.OnNodeRemoved(_createdNodeFID);
	}

	private bool ApplyConnectToSplitArc(HitTestArgs location, IList<FeatureSegmentSubset> hitArcs)
	{
		_completeOperation = CompleteOperation.SplitArc;
		if (!SplitArcOperation.SplitArc(Context.Network, location, hitArcs, Context.AllowOverlappingArcSplits, out _createdNodeFID, out _splitArcs))
		{
			return false;
		}
		Context.OnNodeAdded(_createdNodeFID);
		SplitArcOperation.NotifyArcsAdded(Context, _splitArcs);
		Context.Network.ReconnectArc(_arcIndex, _createdNodeFID, isStart: false);
		return true;
	}

	private void UndoConnectToSplitArc()
	{
		SplitArcOperation.UnsplitArcs(Context.Network, _splitArcs, _createdNodeFID, Context.OnArcRemoved);
		Context.OnNodeRemoved(_createdNodeFID);
	}

	protected override bool UndoInternal()
	{
		UndoSelectionState();
		Context.Network.ReconnectArc(_arcIndex, -1, isStart: false);
		switch (_completeOperation)
		{
		case CompleteOperation.NewNode:
			UndoConnectToNewNode();
			break;
		case CompleteOperation.SplitArc:
			UndoConnectToSplitArc();
			break;
		default:
			throw new NotImplementedException();
		case CompleteOperation.ExistingNode:
			break;
		}
		TryReturnCoincidentInternalPoint();
		return true;
	}
}
