using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;

namespace Ras.Engine.Structures.Gates;

public class Gate : HydraulicStructure
{
	public GatePhysProps PhysProps { get; set; }

	public override bool TreatFlowAsInternalBoundary => false;

	public override HSType StructureType => HSType.Gate;

	public Gate(string name, Polyline cL)
		: base(name, cL)
	{
	}

	public override void DistributeStructure()
	{
	}

	public override void Prepare(SolverControl solverControl, Mesh mesh, MeshPropertyTables meshPropertyTables)
	{
		PhysProps.SetUnitSys(UnitSys);
	}

	public override void Apply(double absTime, double dt, HydraulicStructureCollection hsCollection, MeshWaterState waterState, Mesh mesh, MeshPropertyTables propTables, HydraulicMeshComputer hmesh)
	{
	}
}
