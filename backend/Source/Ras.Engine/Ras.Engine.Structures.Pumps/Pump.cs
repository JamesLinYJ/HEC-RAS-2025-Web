using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine.Structures.Pumps;

public class Pump : HydraulicStructure
{
	public PumpPhysProps PhysProps { get; set; }

	public override bool TreatFlowAsInternalBoundary => false;

	public override HSType StructureType => HSType.Pump;

	public Pump(string name, Polyline cL)
		: base(name, cL)
	{
	}

	public override void DistributeStructure()
	{
	}

	public override void Prepare(SolverControl solverControl, Mesh mesh, MeshPropertyTables meshPropertyTables)
	{
	}

	public override void Apply(double absTime, double dt, HydraulicStructureCollection hsCollection, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh)
	{
	}
}
