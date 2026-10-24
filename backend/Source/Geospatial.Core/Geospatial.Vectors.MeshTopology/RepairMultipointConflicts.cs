using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public class RepairMultipointConflicts
{
	public static class MessageCodes
	{
		public static readonly MessageClass ClearingTinError = new MessageClass("RepairMultipointConfliclts_ClearingTinError", MessageSeverity.Error, "Boundary of conflict area near {Coordinate} could not be triangulated. Check for extra vertices or arc internal points, reduce cell size, split or simplify arcs.");

		public static readonly MessageClass RepairWarning = new MessageClass("RepairMultipointConfliclts_RepairWarning", MessageSeverity.Warning, "Mesh generator had difficulty following arcs, resulting in small or skewed cells near {Coordinate}. Shrinking target cell sizes or moving arc vertices may improve the mesh.");

		public static Message GetClearingTinError(Point pt)
		{
			return ClearingTinError.Create(pt);
		}

		public static Message GetRepairWarning(Point pt)
		{
			return RepairWarning.Create(pt);
		}
	}

	public static void Process(SlidingMesh mesh, ConceptualMesh cmesh, ISizeFunction sizeFunction, OperationResult res)
	{
		if (!RepairByRemoveFace.Process(mesh, sizeFunction, cmesh))
		{
			RepairByClipAndMerge.Process(mesh, sizeFunction, cmesh, logConflicts: true);
		}
	}
}
