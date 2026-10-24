using Geospatial.Vectors;
using Geospatial.Vectors.Topology;

namespace Ras.Synthetics;

public static class TestStructureGenerator
{
	public static Mesh GenDualResolutionMesh(Extent ext, int cellsW, int cellsH)
	{
		Topology topology = new Topology();
		topology.AddNode(new Point(0.0, 190.0));
		topology.AddNode(new Point(0.0, 0.0));
		topology.AddNode(new Point(300.0, 190.0));
		topology.AddNode(new Point(300.0, 0.0));
		topology.AddNode(new Point(600.0, 190.0));
		topology.AddNode(new Point(600.0, 0.0));
		topology.AddArc(0, 1, null);
		topology.AddArc(0, 2, null);
		topology.AddArc(2, 3, null);
		topology.AddArc(3, 1, null);
		topology.AddArc(2, 4, null);
		topology.AddArc(4, 5, null);
		topology.AddArc(5, 3, null);
		topology.AddRegionControlPoint(new Point(150.0, 95.0));
		topology.AddRegionControlPoint(new Point(450.0, 95.0));
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.Quad, SizeFunction.Constant, 100f, 0.2f, 100f, 0f, null, 1f, 1f);
		topology.SetControlPointRecipeString(0, "#Default()");
		topology.SetControlPointTINResolution(0, 1f);
		topology.SetControlPointData(1, PatchType.Quad, SizeFunction.Constant, 100f, 0.2f, 100f, 0f, null, 1f, 1f);
		topology.SetControlPointRecipeString(1, "#Default()");
		topology.SetControlPointTINResolution(1, 1f);
		topology.SetArcData(0, LinearSizeFunction.Constant, 10f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(1, LinearSizeFunction.Constant, 10f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(2, LinearSizeFunction.Constant, 10f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: false);
		topology.SetArcData(3, LinearSizeFunction.Constant, 10f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(4, LinearSizeFunction.Constant, 3f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(5, LinearSizeFunction.Constant, 10f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(6, LinearSizeFunction.Constant, 3f, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out var finalMesh);
		return finalMesh;
	}
}
