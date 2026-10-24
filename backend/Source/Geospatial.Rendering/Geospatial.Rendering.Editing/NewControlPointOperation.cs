using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Utility.DataStructures;

namespace Geospatial.Rendering.Editing;

public class NewControlPointOperation : TopologyEditOperation
{
	private IList<NewControlPointDef> _newPoints;

	private Range1D _newControlPointFIDs;

	public override string Description => "Add Control Point";

	public NewControlPointOperation(TopologyEditContext layer, Point pt, int containedRegionIndex = -1)
		: base(layer, null)
	{
		_newPoints = new CompactList<NewControlPointDef>(new NewControlPointDef(pt, containedRegionIndex));
	}

	public NewControlPointOperation(TopologyEditContext layer, IList<NewControlPointDef> newPts)
		: base(layer, null)
	{
		_newPoints = newPts;
	}

	protected override bool ApplyInternal()
	{
		_newControlPointFIDs = Range1D.NullRange;
		if (_newPoints.Count == 0)
		{
			return true;
		}
		int count = _context.Topology.RegionControlPoints.Count;
		int end = count + _newPoints.Count - 1;
		for (int i = 0; i < _newPoints.Count; i++)
		{
			NewControlPointDef newControlPointDef = _newPoints[i];
			_context.Topology.AddRegionControlPoint(newControlPointDef.Point, out var fid);
			if (_context.Topology.ControlPointMetadata.GetColumn(MeshExtensions.PatchTypeColumn) != null && TryGetAdjacentRegionPatchType(newControlPointDef.ContainedRegionIndex, out var patchType))
			{
				_context.Topology.ControlPointMetadata[fid][MeshExtensions.PatchTypeColumn] = patchType;
			}
		}
		_newControlPointFIDs = new Range1D(count, end);
		_context.OnControlPointsAdded(_newControlPointFIDs.Count);
		return true;
	}

	private bool TryGetAdjacentRegionPatchType(int containedRegionIndex, out PatchType patchType)
	{
		patchType = PatchType.TrianglePaver;
		if (containedRegionIndex < 0 || containedRegionIndex >= _context.Topology.CompletedRegions.Count)
		{
			return false;
		}
		Region region = _context.Topology.CompletedRegions[containedRegionIndex];
		int arcIDArc3Term = -1;
		long minValue = long.MinValue;
		foreach (ArcWithDirection arc3 in region.OuterRing.Arcs)
		{
			Arc arc = _context.Topology.Arcs[arc3.ArcID];
			if (arc.ExternallyTracked_DateOfGenesis > minValue)
			{
				minValue = arc.ExternallyTracked_DateOfGenesis;
				arcIDArc3Term = arc3.ArcID;
			}
		}
		if (arcIDArc3Term < 0)
		{
			return false;
		}
		Arc arc2 = _context.Topology.Arcs[arcIDArc3Term];
		int regionB = ((arc2.RegionA == containedRegionIndex) ? arc2.RegionB : arc2.RegionA);
		if (regionB < 0 || regionB >= _context.Topology.CompletedRegions.Count)
		{
			return false;
		}
		Region region2 = _context.Topology.CompletedRegions[regionB];
		if (region2.ControlPointIndices.Count != 1)
		{
			return false;
		}
		int controlPointIndex = region2.ControlPointIndex;
		patchType = _context.Topology.GetControlPointData(controlPointIndex).PatchType;
		return true;
	}

	protected override bool UndoInternal()
	{
		for (int end = _newControlPointFIDs.End; end >= _newControlPointFIDs.Start; end--)
		{
			_context.Topology.RemoveRegionControlPoint(end);
		}
		_context.OnControlPointsRemoved(Enumerable.Range(_newControlPointFIDs.Start, _newControlPointFIDs.Count));
		UndoSelectionState();
		return true;
	}
}
