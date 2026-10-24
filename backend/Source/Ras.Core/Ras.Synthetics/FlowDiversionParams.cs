using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Synthetics.SyntheticParams;
using Utility.Units;

namespace Ras.Synthetics;

public class FlowDiversionParams : BaseParams
{
	public const double ChannelWidth = 0.3;

	public const double MainLength = 6.0;

	public const double SideLength = 3.0;

	public const double DiversionX = 3.0;

	public static readonly double SideLeftX = 2.85;

	public static readonly double SideRightX = 3.15;

	public static readonly double SideExitY = 3.3;

	public const double CellSizeAcross = 0.01;

	public const double CellSizeAlongMain = 0.02;

	public const double CellSizeAlongSide = 0.03;

	public const double ReachBias = 4.0;

	public static readonly int AcrossCells = (int)Math.Round(30.0);

	public static readonly int ReachUpstreamCells = (int)Math.Round(SideLeftX / 0.02);

	public static readonly int ReachDownstreamCells = (int)Math.Round((6.0 - SideRightX) / 0.02);

	public static readonly int JunctionAlongCells = (int)Math.Round(30.0);

	public static readonly int SideAlongCells = (int)Math.Round(100.0);

	public double InflowDischarge = 0.00567;

	public double MainOutletStage = 0.0555;

	public double SideOutletStage = 0.0465;

	public const float NValueDefault = 0.012f;

	public TurbulenceParams Turbulence = new TurbulenceParams();

	private Mesh _mesh;

	public static readonly double[] MeasuredWall0OverB = new double[18]
	{
		-1.42, -1.25, -1.08, -0.9, -0.72, -0.55, -0.4, -0.25, -0.1, 0.08,
		0.25, 0.42, 0.58, 0.72, 0.88, 1.05, 1.2, 1.35
	};

	public static readonly double[] MeasuredWall0Depth = new double[18]
	{
		0.0512, 0.0511, 0.0512, 0.0513, 0.0516, 0.0521, 0.0528, 0.0537, 0.0548, 0.0553,
		0.0551, 0.0551, 0.0552, 0.0553, 0.0553, 0.0554, 0.0554, 0.0555
	};

	public static readonly double[] MeasuredWall1OverB = new double[24]
	{
		-1.65, -1.48, -1.3, -1.13, -0.97, -0.8, -0.68, -0.55, -0.45, -0.38,
		-0.3, -0.22, -0.12, 0.02, 0.12, 0.22, 0.35, 0.5, 0.62, 0.75,
		0.9, 1.05, 1.2, 1.35
	};

	public static readonly double[] MeasuredWall1Depth = new double[24]
	{
		0.0503, 0.0502, 0.0501, 0.0498, 0.0493, 0.0484, 0.0468, 0.0455, 0.0448, 0.0445,
		0.0435, 0.045, 0.0468, 0.049, 0.0505, 0.0525, 0.056, 0.0578, 0.0575, 0.0578,
		0.057, 0.0568, 0.0568, 0.0566
	};

	public static readonly double[] MeasuredSideRightOverYB = new double[18]
	{
		1.0, 1.17, 1.33, 1.5, 1.67, 1.83, 2.0, 2.17, 2.33, 2.5,
		2.67, 2.83, 3.0, 3.17, 3.33, 3.5, 3.67, 3.83
	};

	public static readonly double[] MeasuredSideRightDepth = new double[18]
	{
		0.057, 0.0549, 0.051, 0.0473, 0.0456, 0.0437, 0.0433, 0.0433, 0.0431, 0.043,
		0.0437, 0.0441, 0.0443, 0.0443, 0.045, 0.045, 0.045, 0.0454
	};

	public static readonly double[] MeasuredSideLeftOverYB = new double[17]
	{
		1.17, 1.33, 1.5, 1.67, 1.83, 2.0, 2.17, 2.33, 2.5, 2.67,
		2.83, 3.0, 3.17, 3.33, 3.5, 3.67, 3.83
	};

	public static readonly double[] MeasuredSideLeftDepth = new double[17]
	{
		0.0437, 0.0433, 0.043, 0.0428, 0.0422, 0.0418, 0.0418, 0.042, 0.0422, 0.043,
		0.0437, 0.0437, 0.0444, 0.0447, 0.0454, 0.0452, 0.0456
	};

	public static readonly IReadOnlyList<MeasuredVelocityComponent> MeasuredMainUProfiles = new _003C_003Ez__ReadOnlyArray<MeasuredVelocityComponent>(new MeasuredVelocityComponent[5]
	{
		new MeasuredVelocityComponent("X/B=-0.5", -0.5, new double[9] { 0.03, 0.1, 0.24, 0.37, 0.5, 0.65, 0.75, 0.9, 0.97 }, new double[9] { 0.255, 0.305, 0.345, 0.375, 0.4, 0.41, 0.435, 0.45, 0.455 }),
		new MeasuredVelocityComponent("X/B=0.0", 0.0, new double[9] { 0.03, 0.1, 0.24, 0.37, 0.5, 0.63, 0.76, 0.9, 0.97 }, new double[9] { 0.165, 0.215, 0.27, 0.29, 0.3, 0.315, 0.3, 0.31, 0.295 }),
		new MeasuredVelocityComponent("X/B=0.5", 0.5, new double[8] { 0.1, 0.23, 0.38, 0.51, 0.63, 0.76, 0.86, 0.92 }, new double[8] { 0.095, 0.18, 0.19, 0.21, 0.21, 0.2, 0.155, 0.14 }),
		new MeasuredVelocityComponent("X/B=1.5", 1.5, new double[7] { 0.24, 0.37, 0.5, 0.63, 0.77, 0.9, 0.97 }, new double[7] { 0.09, 0.155, 0.2, 0.195, 0.235, 0.27, 0.275 }),
		new MeasuredVelocityComponent("X/B=7.0", 7.0, new double[9] { 0.04, 0.11, 0.24, 0.37, 0.5, 0.64, 0.77, 0.91, 0.97 }, new double[9] { 0.08, 0.1, 0.115, 0.15, 0.165, 0.2, 0.21, 0.21, 0.175 })
	});

	public static readonly IReadOnlyList<MeasuredVelocityComponent> MeasuredSideVProfiles = new _003C_003Ez__ReadOnlyArray<MeasuredVelocityComponent>(new MeasuredVelocityComponent[3]
	{
		new MeasuredVelocityComponent("Y/B=1.65", 1.65, new double[9] { -0.49, -0.46, -0.08, -0.01, 0.08, 0.16, 0.26, 0.42, 0.47 }, new double[9] { -0.1, -0.11, 0.18, 0.31, 0.37, 0.45, 0.52, 0.58, 0.55 }),
		new MeasuredVelocityComponent("Y/B=2.65", 2.65, new double[7] { -0.17, -0.08, 0.01, 0.13, 0.26, 0.42, 0.47 }, new double[7] { 0.16, 0.22, 0.32, 0.43, 0.48, 0.57, 0.56 }),
		new MeasuredVelocityComponent("Y/B=3.65", 3.65, new double[8] { -0.17, -0.08, 0.0, 0.08, 0.13, 0.27, 0.42, 0.47 }, new double[8] { 0.17, 0.21, 0.27, 0.32, 0.355, 0.41, 0.47, 0.47 })
	});

	public override UnitSystem Units => UnitSystem.SI;

	public override List<string> Tags
	{
		get
		{
			int ordinal = 8;
			List<string> stringList = new List<string>(ordinal);
			CollectionsMarshal.SetCount(stringList, ordinal);
			Span<string> stringListSpan = CollectionsMarshal.AsSpan(stringList);
			int ordinal2 = 0;
			stringListSpan[ordinal2] = "2D";
			ordinal2++;
			stringListSpan[ordinal2] = "Diversion";
			ordinal2++;
			stringListSpan[ordinal2] = "Bifurcation";
			ordinal2++;
			stringListSpan[ordinal2] = "Junction";
			ordinal2++;
			stringListSpan[ordinal2] = "Shettar";
			ordinal2++;
			stringListSpan[ordinal2] = "Murthy";
			ordinal2++;
			stringListSpan[ordinal2] = "SRH-W";
			ordinal2++;
			stringListSpan[ordinal2] = "Validation";
			return stringList;
		}
	}

	public override Extent SystemExtent => CreateMesh().Extent;

	public static FlowDiversionParams Default()
	{
		return new FlowDiversionParams
		{
			NValue = 0.012f,
			InflowDischarge = 0.00567,
			MainOutletStage = 0.0555,
			SideOutletStage = 0.0465,
			SolveDt = 0.01,
			SolveDuration = 120.0,
			ReportFrequency = 100
		};
	}

	public override float TerrainElevation(Point pt)
	{
		return 0f;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point p) => (float)MainOutletStage;
	}

	public override void InitPlanOptions(Plan pl)
	{
		base.InitPlanOptions(pl);
		pl.SolverType = SolverType.CPU;
		pl.UseDiffusion = Turbulence.IsActive;
		pl.TurbulenceModel = Turbulence.Model;
		pl.BaseEddyViscosity = Turbulence.BaseEddyViscosity;
		pl.LongMixCoeff = Turbulence.LongMixCoeff;
		pl.TransMixCoeff = Turbulence.TransMixCoeff;
		pl.HorizMixCoeff = Turbulence.HorizMixCoeff;
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.dtMinMicros = 1000L;
		sc.Turbulence = Turbulence;
	}

	public override bool TryCreateTopology(out Topology topology)
	{
		topology = MakeTopology();
		return true;
	}

	public override Mesh CreateMesh()
	{
		if (_mesh == null)
		{
			Topology topology = MakeTopology();
			topology.Complete();
			topology.BuildConceptualMesh(out var _, out _mesh);
		}
		return _mesh;
	}

	public static Topology MakeTopology()
	{
		Topology topology = new Topology();
		topology.AddNode(new Point(0.0, 0.0));
		topology.AddNode(new Point(SideLeftX, 0.0));
		topology.AddNode(new Point(SideRightX, 0.0));
		topology.AddNode(new Point(6.0, 0.0));
		topology.AddNode(new Point(6.0, 0.3));
		topology.AddNode(new Point(SideRightX, 0.3));
		topology.AddNode(new Point(SideLeftX, 0.3));
		topology.AddNode(new Point(0.0, 0.3));
		topology.AddNode(new Point(SideLeftX, SideExitY));
		topology.AddNode(new Point(SideRightX, SideExitY));
		topology.AddArc(0, 1, null);
		topology.AddArc(1, 2, null);
		topology.AddArc(2, 3, null);
		topology.AddArc(3, 4, null);
		topology.AddArc(4, 5, null);
		topology.AddArc(5, 2, null);
		topology.AddArc(5, 6, null);
		topology.AddArc(6, 1, null);
		topology.AddArc(6, 7, null);
		topology.AddArc(7, 0, null);
		topology.AddArc(6, 8, null);
		topology.AddArc(8, 9, null);
		topology.AddArc(9, 5, null);
		topology.AddRegionControlPoint(new Point(0.5 * SideLeftX, 0.15));
		topology.AddRegionControlPoint(new Point(3.0, 0.15));
		topology.AddRegionControlPoint(new Point(0.5 * (SideRightX + 6.0), 0.15));
		topology.AddRegionControlPoint(new Point(3.0, 1.8));
		topology.EnsureMeshMetadataColumns();
		for (int i = 0; i < 4; i++)
		{
			topology.SetControlPointData(i, PatchType.Quad, SizeFunction.Constant, float.NaN);
			topology.SetControlPointTINResolution(i, -1f);
		}
		SetCount(topology, 0, ReachUpstreamCells, 0.25);
		SetCount(topology, 1, JunctionAlongCells);
		SetCount(topology, 2, ReachDownstreamCells, 4.0);
		SetCount(topology, 3, AcrossCells);
		SetCount(topology, 4, ReachDownstreamCells, 0.25);
		SetCount(topology, 5, AcrossCells);
		SetCount(topology, 6, JunctionAlongCells);
		SetCount(topology, 7, AcrossCells);
		SetCount(topology, 8, ReachUpstreamCells, 4.0);
		SetCount(topology, 9, AcrossCells);
		SetCount(topology, 10, SideAlongCells, 4.0);
		SetCount(topology, 11, JunctionAlongCells);
		SetCount(topology, 12, SideAlongCells, 0.25);
		return topology;
	}

	private static void SetCount(Topology topo, int arcIdx, int count, double bias = 1.0)
	{
		topo.SetArcData(arcIdx, LinearSizeFunction.CountWithBias, float.NaN, count, (float)bias);
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		double computed = 0.01;
		double computed2 = 0.015;
		Polyline pl = new Polyline
		{
			{
				0.0 - computed,
				0.0
			},
			{
				0.0 - computed,
				0.3
			}
		};
		Polyline pl2 = new Polyline
		{
			{
				6.0 + computed,
				0.0
			},
			{
				6.0 + computed,
				0.3
			}
		};
		Polyline pl3 = new Polyline
		{
			{
				SideLeftX,
				SideExitY + computed2
			},
			{
				SideRightX,
				SideExitY + computed2
			}
		};
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pl, InflowDischarge);
		boundaryConditionLine.Name = "Inflow";
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantStage(bc, pl2, MainOutletStage);
		boundaryConditionLine2.Name = "MainOutlet";
		BoundaryConditionLine boundaryConditionLine3 = BoundaryConditionLine.ConstantStage(bc, pl3, SideOutletStage);
		boundaryConditionLine3.Name = "SideOutlet";
		int ordinal = 3;
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>(ordinal);
		CollectionsMarshal.SetCount(boundaryConditionLines, ordinal);
		Span<BoundaryConditionLine> boundaryConditionLinesSpan = CollectionsMarshal.AsSpan(boundaryConditionLines);
		int ordinal2 = 0;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine;
		ordinal2++;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine2;
		ordinal2++;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine3;
		return boundaryConditionLines;
	}

	public override IReadOnlyList<MeasuredStageProfile> GetMeasuredStageProfiles()
	{
		return new _003C_003Ez__ReadOnlyArray<MeasuredStageProfile>(new MeasuredStageProfile[2]
		{
			BuildWallProfile("Measured Y/B=0 Wall", 0.0, MeasuredWall0OverB, MeasuredWall0Depth),
			BuildWallProfile("Measured Y/B=1 Wall", 0.3, MeasuredWall1OverB, MeasuredWall1Depth)
		});
	}

	public override IReadOnlyList<NamedResultLine> GetResultLines()
	{
		return new _003C_003Ez__ReadOnlyArray<NamedResultLine>(new NamedResultLine[2]
		{
			new NamedResultLine(WallLine(0.0), "Y/B=0 Wall"),
			new NamedResultLine(WallLine(0.3), "Y/B=1 Wall")
		});
	}

	private static Polyline WallLine(double wallY)
	{
		return new Polyline
		{
			{
				OverBToX(-2.0),
				wallY
			},
			{
				OverBToX(2.0),
				wallY
			}
		};
	}

	private static MeasuredStageProfile BuildWallProfile(string name, double wallY, double[] overB, double[] depth)
	{
		double[] doubleValues = new double[overB.Length];
		for (int i = 0; i < overB.Length; i++)
		{
			doubleValues[i] = (overB[i] + 2.0) / 4.0;
		}
		return MeasuredStageProfile.FromFractions(name, WallLine(wallY), doubleValues, depth);
	}

	public static double OverBToX(double xOverB)
	{
		return 3.0 + xOverB * 0.3;
	}

	public static double OverBToY(double yOverB)
	{
		return yOverB * 0.3;
	}
}
