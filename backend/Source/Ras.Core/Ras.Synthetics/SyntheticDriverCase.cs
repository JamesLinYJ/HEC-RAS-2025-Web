using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Engine.Structures;
using Utility.Logging;
using Utility.Units;

namespace Ras.Synthetics;

public class SyntheticDriverCase
{
	public MeshPropertyTables Props;

	public MeshWaterState InitialState;

	public BoundaryConditionCollection BoundaryConditions;

	public HydraulicStructureCollection HydraulicStructures;

	public Mesh Mesh;

	public ISolver Solver;

	public ComputeDriver ComputeDriver;

	public Topology Topology;

	public UnitSystem UnitSystem = UnitSystem.SI;

	public ComputeControl ComputeControl = new ComputeControl();

	public List<SolverControl> SolverControls = new List<SolverControl>();

	public Dictionary<string, object> Parameters = new Dictionary<string, object>();

	private OperationResult OpRes = new OperationResult();

	public Func<Dictionary<string, object>, Topology> TopologyFactory;

	public IResample<float> ResampleTerrain;

	public Func<Point, float> FuncTerrain;

	public Func<Point, float> FuncManningsN;

	public IResample<float> ResampleManningsN;

	public Func<Point, double, double> AnaStageFunc;

	public Func<SyntheticDriverCase, Func<Point, double, double>> AnaStageFuncFactory;

	public Func<Point, double, Vector> AnaVelocityFunc;

	public Func<SyntheticDriverCase, Func<Point, double, Vector>> AnaVelocityFuncFactory;

	public Action<BoundaryConditionCollection, SyntheticDriverCase> SetBoundaryConditionFunc;

	public IResample<float> GetTerrain()
	{
		if (ResampleTerrain != null)
		{
			return ResampleTerrain;
		}
		if (FuncTerrain == null)
		{
			throw new Exception("One of ResampleTerrain or FuncTerrina must be specified");
		}
		return new FunctionalResampler<float>(Mesh.Extent, -9999f, FuncTerrain);
	}

	public IResample<float> GetManningsN()
	{
		if (ResampleManningsN != null)
		{
			return ResampleManningsN;
		}
		if (FuncManningsN == null)
		{
			throw new Exception("One of ResampleManningsN or FuncMannginsN must be specified");
		}
		return new FunctionalResampler<float>(Mesh.Extent, -9999f, FuncManningsN);
	}

	public ComputeDriver PrepareRunnableDriver()
	{
		PrepareSolverControls();
		PrepareMesh();
		PrepareMeshPropertyTables();
		PrepareInitialCondition();
		PrepareComputeDriver();
		PrepareBoundaryConditions();
		return ComputeDriver;
	}

	private void PrepareSolverControls()
	{
		if (SolverControls.Count == 0)
		{
			SolverControls.Add(new SolverControl());
		}
		if (SolverControls.Count > 1)
		{
			throw new Exception("Not ready for SyntheticPlan with multiple solvers");
		}
	}

	private void PrepareMesh()
	{
		if (Topology == null)
		{
			Topology = TopologyFactory(Parameters);
		}
		OpRes.MergeIn(Topology.Complete());
		if (Mesh == null)
		{
			OpRes.MergeIn(Topology.BuildConceptualMesh(out var _, out Mesh));
		}
		else
		{
			OpRes.AddMessage(new Message("Mesh was specified independent of Topology. Good luck!"));
		}
	}

	private void PrepareMeshPropertyTables()
	{
		Mesh.Extent.Extend(0.1);
		IResample<float> terrain = GetTerrain();
		IResample<float> manningsN = GetManningsN();
		OpRes.MergeIn(MeshPropertyTables.ComputeFrom(Mesh, terrain, manningsN, PropertyTableOptions.Default(UnitSystem), out Props));
	}

	private void PrepareComputeDriver()
	{
		if (SolverControls.Count == 1)
		{
			ComputeDriver = new ComputeDriver(Mesh, Props, InitialState, ComputeControl, SolverControls[0]);
			ComputeDriver.MeshTags = MeshTags.ComputeFromTopology(Mesh, Topology);
			return;
		}
		ComputeDriver = new ComputeDriver(Mesh, Props, InitialState, ComputeControl, SolverControls, null);
		throw new NotImplementedException("Multiple solvers not yet implemented");
	}

	private void PrepareInitialCondition()
	{
		double computed = 0.0;
		InitialState = new MeshWaterState(Mesh, Props);
		if (AnaStageFunc == null)
		{
			AnaStageFunc = AnaStageFuncFactory(this);
		}
		for (int i = 0; i < InitialState.CellCount; i++)
		{
			Point cellCenter = Mesh.CellCenters[i];
			double anaStageFunc = AnaStageFunc(cellCenter, computed);
			InitialState.CellVolume[i] = anaStageFunc * Mesh.CellPolygonArea(i);
		}
		if (AnaVelocityFunc == null)
		{
			AnaVelocityFunc = AnaVelocityFuncFactory(this);
		}
		for (int j = 0; j < InitialState.FaceCount; j++)
		{
			Face face = Mesh.Faces[j];
			if (face.cellA >= 0 && face.cellB >= 0)
			{
				Vector other = -1.0 * Mesh.FaceSegment(j).PerpendicularUnitVector();
				Point faceMidPoint = Mesh.FaceMidPoint(j);
				Vector vector = AnaVelocityFunc(faceMidPoint, computed);
				double elev = AnaStageFunc(faceMidPoint, computed);
				double interpolateFaceArea = Props.InterpolateFaceArea(j, elev);
				InitialState.FaceVelocity[j] = vector.Dot(other);
				InitialState.FaceFlow[j] = interpolateFaceArea * InitialState.FaceVelocity[j];
			}
		}
		InitialState.UpdateCells(null, null, null);
	}

	private void PrepareBoundaryConditions()
	{
		if (SetBoundaryConditionFunc != null)
		{
			SetBoundaryConditionFunc(BoundaryConditions, this);
		}
	}
}
