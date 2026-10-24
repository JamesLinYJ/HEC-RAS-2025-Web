using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Meshes;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Engine.Structures;
using Ras.Hydraulics;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Logging;
using Utility.Units;

namespace Ras.Synthetics;

public class SinuousChannelTestCase
{
	public double periodX = 1000.0;

	public double amplitude = 400.0;

	public double nPeriods = 4.0;

	public double channelW = 80.0;

	public int channelCellsAcross = 1;

	public int channelCellLength = 150;

	public double valleyW = 2000.0;

	public double manningsN = 0.01;

	public double curvePower = 0.5;

	public UnitSystem units;

	private double slopeY = 0.0001;

	private double slopeX = 1E-06;

	public bool streamwise = true;

	public Polyline centerline;

	public Polyline leftBank;

	public Polyline rightBank;

	public double rampSeconds;

	public double upstreamFlow = 200.0;

	public double upstreamEGSlope = 1E-05;

	public double downstreamStage = -1.0;

	public int[] leftFaces;

	public int[] rightFaces;

	public MeshPropertyTables Props;

	public MeshWaterState InitialState;

	public BoundaryConditionCollection BoundaryConditions;

	public HydraulicStructureCollection HydraulicStructures;

	public Mesh Mesh;

	public Solver Solver;

	public ComputeDriver Driver;

	public double Length => nPeriods * periodX;

	public Mesh CreateMesh()
	{
		int ordinal = 180;
		Topology topology = new Topology();
		centerline = new Polyline();
		leftBank = new Polyline();
		rightBank = new Polyline();
		int count = 1 + (int)((double)ordinal * nPeriods);
		for (int i = 0; i < count; i++)
		{
			double xComponent = periodX * (double)i / (double)ordinal;
			double xComponentPiTerm = Math.Cos(xComponent * 2.0 * Math.PI / periodX);
			Point point = new Point(xComponent, amplitude * (double)Math.Sign(xComponentPiTerm) * Math.Pow(Math.Abs(xComponentPiTerm), curvePower));
			centerline.Add(point);
			Vector vector = new Vector(Math.PI * 2.0 / periodX * amplitude * Math.Sin(xComponent * 2.0 * Math.PI / periodX), 1.0).UnitVector();
			leftBank.Add(point + channelW / 2.0 * vector);
			rightBank.Add(point - channelW / 2.0 * vector);
		}
		topology.AddNode(leftBank[0]);
		topology.AddNode(rightBank[0]);
		topology.AddNode(rightBank.Last());
		topology.AddNode(leftBank.Last());
		topology.AddNode(new Point(0.0, (0.0 - valleyW) / 2.0));
		topology.AddNode(new Point(rightBank.Last().X, (0.0 - valleyW) / 2.0));
		topology.AddNode(new Point(leftBank.Last().X, valleyW / 2.0));
		topology.AddNode(new Point(0.0, valleyW / 2.0));
		topology.AddArc(0, 1, null);
		topology.AddArc(1, 2, rightBank.GetRange(1, rightBank.Count - 1));
		topology.AddArc(2, 3, null);
		topology.AddArc(0, 3, leftBank.GetRange(1, leftBank.Count - 1));
		topology.AddArc(1, 4, null);
		topology.AddArc(4, 5, null);
		topology.AddArc(5, 2, null);
		topology.AddArc(3, 6, null);
		topology.AddArc(6, 7, null);
		topology.AddArc(7, 0, null);
		topology.AddRegionControlPoint(centerline[1]);
		topology.AddRegionControlPoint(rightBank[1] - new Vector(0.0, valleyW / 4.0));
		topology.AddRegionControlPoint(leftBank[1] + new Vector(0.0, valleyW / 4.0));
		topology.EnsureMeshMetadataColumns();
		double channelWOverChannelCellsAcross = channelW / (double)channelCellsAcross;
		int scaledChannelCellLength = 4 * channelCellLength;
		topology.SetControlPointData(0, PatchType.Quad, SizeFunction.Constant, (float)channelWOverChannelCellsAcross, 100f, 1f, float.NaN, null, 1f, 1f);
		if (streamwise)
		{
			topology.SetControlPointRecipeString(0, "#tag STREAMWISECOORDINATES\nDefault()");
		}
		else
		{
			topology.SetControlPointRecipeString(0, "Default()");
		}
		topology.SetControlPointData(1, PatchType.TrianglePaver, SizeFunction.Constant, scaledChannelCellLength, 0.1f);
		topology.SetControlPointRecipeString(1, "Default()");
		topology.SetControlPointData(2, PatchType.TrianglePaver, SizeFunction.Constant, scaledChannelCellLength, 0.1f);
		topology.SetControlPointRecipeString(2, "Default()");
		topology.SetArcData(0, LinearSizeFunction.CountWithBias, (float)channelWOverChannelCellsAcross, channelCellsAcross, 1f, ConstrainStretchingOptions.Floating, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(2, LinearSizeFunction.CountWithBias, (float)channelWOverChannelCellsAcross, channelCellsAcross, 1f, ConstrainStretchingOptions.Floating, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(1, LinearSizeFunction.Constant, channelCellLength, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		topology.SetArcData(3, LinearSizeFunction.Constant, channelCellLength, 8, 1f, ConstrainStretchingOptions.Constrained, distanceStraightLine: true, 0f, 0.1f, followExactly: true);
		for (int j = 4; j < topology.Arcs.Count; j++)
		{
			topology.SetArcData(j, LinearSizeFunction.Constant, scaledChannelCellLength, -1, 1f, ConstrainStretchingOptions.Constrained);
		}
		topology.Complete();
		topology.BuildConceptualMesh(out var _, out var finalMesh);
		return finalMesh;
	}

	public virtual ComputePackage GenerateDriverPackage(SolverControl solverControl = null)
	{
		GeneratePackageCommon(out var elevs, out var nvals);
		if (solverControl == null)
		{
			solverControl = new SolverControl();
		}
		ComputeControl computeControl = new ComputeControl();
		UpdateControl(computeControl, solverControl);
		int[] intValues = new int[Mesh.CellCount];
		for (int i = 0; i < intValues.Length; i++)
		{
			intValues[i] = 0;
		}
		Driver = new ComputeDriver(Mesh, Props, InitialState, null, new List<SolverControl> { solverControl }, intValues);
		BoundaryConditions = Driver.BoundaryConditions;
		setBCs();
		HydraulicStructures = Driver.HydraulicStructures;
		addStructures();
		Driver.HydraulicStructures.IdentifyStructureCellsAndFaces(Mesh);
		return new ComputePackage(Driver, Mesh, Props, elevs, nvals);
	}

	public virtual Result GenerateResult()
	{
		GeneratePackageCommon(out var _, out var _);
		Geometry g = GenerateGeometry();
		Ras.Layers.BoundaryCondition bc = GenerateBoundaryCondition();
		Plan plan = Plan.CreateInMemory("SinuousPlan");
		plan.ComputeTimestep = TimeSpan.FromSeconds(60L);
		plan.TimeWindowMode = PlanTimeWindowMode.Plan;
		plan.InternalComputeDurationSeconds = 172800.0;
		plan.SetOutputFrequency(15);
		Result result = Result.PrepareResult("C:\\Temp\\sinuous_result.h5", plan, g, bc, InitialState);
		result.EmbeddedUnitSystem = units;
		return result;
	}

	public virtual Geometry GenerateGeometry()
	{
		Geometry geometry = Geometry.CreateInMemory("memoryGeom");
		FlowArea flowArea = new FlowArea(Mesh, "sinuousMesh")
		{
			PropertyTables = Props
		};
		geometry.FlowAreaLayer.Add(flowArea);
		return geometry;
	}

	public Polyline UpstreamBCLine()
	{
		return new Polyline
		{
			new Point(0.0, valleyW / 2.0),
			new Point(0.0, (0.0 - valleyW) / 2.0)
		};
	}

	public Polyline DownstreamBCLine()
	{
		return new Polyline
		{
			new Point(Length, valleyW / 2.0),
			new Point(Length, (0.0 - valleyW) / 2.0)
		};
	}

	public virtual Ras.Layers.BoundaryCondition GenerateBoundaryCondition()
	{
		Ras.Layers.BoundaryCondition boundaryCondition = Ras.Layers.BoundaryCondition.CreateInMemory("SinuousBC");
		boundaryCondition.BoundaryConditionLines.Add(BoundaryConditionLine.ConstantFlow(boundaryCondition, UpstreamBCLine(), upstreamFlow));
		boundaryCondition.BoundaryConditionLines.Add(BoundaryConditionLine.ConstantStage(boundaryCondition, DownstreamBCLine(), downstreamStage));
		boundaryCondition.InitialWaterSurface = new ConstantValueResampler<float>((float)downstreamStage, Mesh.Extent);
		return boundaryCondition;
	}

	public double FindY(Polyline pl, double x)
	{
		int intervalIndex = 0;
		int countOffset = pl.Count - 1;
		if (x <= pl[intervalIndex].X)
		{
			return pl[intervalIndex].Y;
		}
		if (x >= pl[countOffset].X)
		{
			return pl[countOffset].Y;
		}
		for (int plCount = pl.Count; plCount >= 0; plCount--)
		{
			int ordinal = (intervalIndex + countOffset) / 2;
			if (intervalIndex == ordinal)
			{
				double submergenceRatio = (x - pl[intervalIndex].X) / (pl[countOffset].X - pl[intervalIndex].X);
				return pl[intervalIndex].Y + submergenceRatio * (pl[countOffset].Y - pl[intervalIndex].Y);
			}
			if (x < pl[ordinal].X)
			{
				countOffset = ordinal;
			}
			else
			{
				intervalIndex = ordinal;
			}
		}
		throw new Exception("Bisection failed");
	}

	public float FuncSurface(Point p)
	{
		double findY = FindY(leftBank, p.X);
		double findY2 = FindY(rightBank, p.X);
		double slopeXLeftBankTerm = slopeX * (leftBank.Last().X - p.X);
		double computed = 2.0;
		double slopeYFindYTerm;
		if (p.Y > findY - computed)
		{
			slopeYFindYTerm = slopeY * (p.Y - findY);
		}
		else
		{
			slopeYFindYTerm = ((!(p.Y < findY2 + computed)) ? (-10.0) : ((0.0 - slopeY) * (p.Y - findY)));
		}
		return (float)(slopeXLeftBankTerm + slopeYFindYTerm);
	}

	private void GeneratePackageCommon(out IResample<float> elevs, out IResample<float> nvals)
	{
		Mesh = CreateMesh();
		if (Mesh == null || !Mesh.IsValid())
		{
			throw new Exception("Mesh generation problem.");
		}
		Extent ext = Mesh.Extent.Extend(0.1);
		elevs = new FunctionalResampler<float>(ext, -9999f, FuncSurface);
		nvals = new ConstantValueResampler<float>((float)manningsN, ext);
		OperationResult operationResult = MeshPropertyTables.ComputeFrom(Mesh, elevs, nvals, PropertyTableOptions.Default(UnitSystem.USCustomary), out Props);
		if (!operationResult)
		{
			throw new Exception("Could not compute property tables: " + operationResult.GetConcatenatedMessages());
		}
		SetLeftFaces(Mesh.Extent);
		SetRightFaces(Mesh.Extent);
		InitialState = new MeshWaterState(Mesh, Props);
		UpdateInitial(InitialState);
	}

	public virtual void SetLeftFaces(Extent ext)
	{
		Polyline pline = Polyline.FromSegment(ext.LeftWall.Scale(0.01, 0.99));
		leftFaces = (from f in Mesh.PerimeterFacesAlongPolyline(pline, out var _)
			select f.Face).ToArray();
	}

	public virtual void SetRightFaces(Extent ext)
	{
		Polyline pline = Polyline.FromSegment(ext.RightWall.Scale(0.01, 0.99));
		rightFaces = (from f in Mesh.PerimeterFacesAlongPolyline(pline, out var _)
			select f.Face).ToArray();
	}

	public virtual void UpdateControl(ComputeControl computeControl, SolverControl solverControl)
	{
	}

	public virtual void setBCs()
	{
		SetUpstreamBC();
		SetDownstreamBC();
	}

	public virtual void addStructures()
	{
	}

	public virtual void UpdateInitial(MeshWaterState state)
	{
		state.SetConstantElevation(downstreamStage);
	}

	public virtual void SetUpstreamBC()
	{
		Ras.Engine.BoundaryCondition boundaryCondition = null;
		if (rampSeconds == 0.0)
		{
			boundaryCondition = Ras.Engine.BoundaryCondition.ConstantFaceValue(leftFaces, upstreamFlow);
		}
		else
		{
			boundaryCondition = new Ras.Engine.BoundaryCondition();
			boundaryCondition.Faces = leftFaces;
			boundaryCondition.FaceFractions = Enumerable.Repeat(1.0 / (double)leftFaces.Length, leftFaces.Length).ToArray();
			boundaryCondition.Timesteps = new double[4] { -1E+32, 0.0, rampSeconds, 1E+32 };
			boundaryCondition.Values = new double[4] { 0.0, 0.0, upstreamFlow, upstreamFlow };
		}
		boundaryCondition.EGSlope = upstreamEGSlope;
		BoundaryConditions.FlowBoundaryConditions.Add(boundaryCondition);
	}

	public virtual void SetDownstreamBC()
	{
		Ras.Engine.BoundaryCondition constantFaceValue = Ras.Engine.BoundaryCondition.ConstantFaceValue(rightFaces, downstreamStage);
		BoundaryConditions.StageBoundaryConditions.Add(constantFaceValue);
	}
}
