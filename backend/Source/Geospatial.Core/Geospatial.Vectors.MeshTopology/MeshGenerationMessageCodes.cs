using System;
using Utility.Logging;

namespace Geospatial.Vectors.MeshTopology;

public static class MeshGenerationMessageCodes
{
	public static readonly MessageClass GenericLoadTime = new MessageClass("Generic_TaskLoadTime", MessageSeverity.Information, "{Task} loaded in {TimeSpan}.");

	public static readonly MessageClass ErrorTriangulatingGlobalSizeFunction = new MessageClass("MeshGen_TriangulatingGlobalSizeFunction", MessageSeverity.Error, "Triangulation for global size function failed. Check narrow quad patch with coarse resolution.");

	public static readonly MessageClass ErrorMergingRegionMeshes = new MessageClass("MeshGen_MergingRegionMeshes", MessageSeverity.Error, "Region {ControlPointIndex} (Internal FID {RegionIndex}) failed to merge.");

	public static readonly MessageClass ErrorGeneratedTopology = new MessageClass("MeshGen_GeneratedTopology", MessageSeverity.Error, "Internal failure in mesh generation created invalid topology near {pnt}.");

	public static Message GetGenericLoadTime(string task, TimeSpan ts)
	{
		return GenericLoadTime.Create(task, ts);
	}

	public static Message GetErrorTriangulatingGlobalSizeFunction()
	{
		return ErrorTriangulatingGlobalSizeFunction.Create();
	}

	public static Message GetErrorMergingRegionMeshes(int ctrlPtIdx, int regionIdx)
	{
		return ErrorMergingRegionMeshes.Create(ctrlPtIdx, regionIdx);
	}

	public static Message GetErrorGeneratedTopology(Point pnt)
	{
		return ErrorGeneratedTopology.Create(pnt);
	}
}
