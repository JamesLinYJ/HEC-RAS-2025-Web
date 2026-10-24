using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;
using Utility.Units;

namespace Ras.Synthetics;

public class SpurDikeParams : BaseParams
{
	public const double DomainLength = 10.0;

	public const double DomainWidth = 0.92;

	public const double DikeX = 4.0;

	public const double DikeLength = 0.1524;

	public const double TerrainResolution = 0.01;

	public const double DikeWidthX = 0.03;

	public const float DikeCrest = 0.3f;

	public const double CellSizeX = 0.08;

	public const double CellSizeY = 0.04;

	public static readonly int CellsAcross = (int)Math.Round(23.0);

	public double UpstreamFlow = 0.0453;

	public double DownstreamStage = 0.189;

	public double RampSeconds = 7200.0;

	public TurbulenceParams Turbulence = new TurbulenceParams();

	private Mesh _mesh;

	private IResample<float> _terrain;

	public override UnitSystem Units => UnitSystem.SI;

	public override Extent SystemExtent => CreateMesh().Extent;

	public static SpurDikeParams Default()
	{
		return new SpurDikeParams
		{
			NValue = 0.01f,
			UpstreamFlow = 0.0453,
			DownstreamStage = 0.189,
			RampSeconds = 7200.0,
			SolveDt = 1.0,
			SolveDuration = 10800.0,
			ReportFrequency = 1,
			Turbulence = new TurbulenceParams
			{
				BaseEddyViscosity = 0.0,
				LongMixCoeff = 0.1,
				TransMixCoeff = 0.02,
				HorizMixCoeff = 0.0
			}
		};
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

	public override bool TryCreateTopology(out Topology topology)
	{
		topology = MakeTopology();
		return true;
	}

	public override float TerrainElevation(Point pt)
	{
		double computed = 0.015;
		bool ptTerm = pt.X >= 4.0 - computed && pt.X <= 4.0 + computed;
		bool ptTerm2 = pt.Y >= 0.0 && pt.Y <= 0.1524;
		if (ptTerm & ptTerm2)
		{
			return 0.3f;
		}
		return 0f;
	}

	public override IResample<float> TerrainResampler()
	{
		if (_terrain == null)
		{
			Extent extent = CreateMesh().Extent.Extend(0.1);
			RasterDefinition rasterDefinition = new RasterDefinition(extent, 0.01);
			FunctionalResampler<float> functionalResampler = new FunctionalResampler<float>(extent, -9999f, TerrainElevation);
			float[] floatValues = new float[rasterDefinition.CellCount];
			functionalResampler.Resample(new ResampleArgs(rasterDefinition), floatValues);
			_terrain = new RasterData<float>(rasterDefinition, floatValues)
			{
				NoDataValue = -9999f
			};
		}
		return _terrain;
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return (Point p) => (float)DownstreamStage;
	}

	public override void InitPlanOptions(Plan pl)
	{
		base.InitPlanOptions(pl);
		pl.SolverType = SolverType.CPU;
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.Turbulence = Turbulence;
		sc.dtMinMicros = 1000L;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		double computed = 0.04;
		Polyline pline = new Polyline
		{
			{
				0.0 - computed,
				0.0
			},
			{
				0.0 - computed,
				0.92
			}
		};
		Polyline pline2 = new Polyline
		{
			{
				10.0 + computed,
				0.0
			},
			{
				10.0 + computed,
				0.92
			}
		};
		return new List<BoundaryConditionLine>
		{
			BuildInflow(bc, pline),
			BuildOutflow(bc, pline2)
		};
	}

	private BoundaryConditionLine BuildInflow(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		if (RampSeconds <= 0.0)
		{
			BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantFlow(bc, pline, UpstreamFlow);
			boundaryConditionLine.Name = "Upstream";
			return boundaryConditionLine;
		}
		BoundaryConditionLine boundaryConditionLine2 = new BoundaryConditionLine(bc, pline)
		{
			Name = "Upstream"
		};
		TimeSpan ts = TimeSpan.FromMinutes(1L);
		double totalSeconds = ts.TotalSeconds;
		int count = (int)Math.Ceiling(SolveDuration / totalSeconds);
		double rampSeconds = Math.Max(totalSeconds, Math.Ceiling(RampSeconds / totalSeconds) * totalSeconds);
		double[] doubleValues = new double[count + 1];
		for (int i = 0; i <= count; i++)
		{
			double time = (double)i * totalSeconds;
			doubleValues[i] = UpstreamFlow * BaseParams.RampFactor(time, rampSeconds);
		}
		boundaryConditionLine2.Payloads.Add(new TimeSeriesPayload("Flow", new RegularTimeSeriesDefinition(ts), doubleValues));
		return boundaryConditionLine2;
	}

	private BoundaryConditionLine BuildOutflow(Ras.Layers.BoundaryCondition bc, Polyline pline)
	{
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.ConstantStage(bc, pline, DownstreamStage);
		boundaryConditionLine.Name = "Downstream";
		return boundaryConditionLine;
	}

	public static Topology MakeTopology()
	{
		int count = (int)Math.Round(50.0);
		int count2 = (int)Math.Round(75.0);
		Topology topology = new Topology();
		topology.AddNode(new Point(0.0, 0.0));
		topology.AddNode(new Point(4.0, 0.0));
		topology.AddNode(new Point(10.0, 0.0));
		topology.AddNode(new Point(10.0, 0.92));
		topology.AddNode(new Point(4.0, 0.92));
		topology.AddNode(new Point(0.0, 0.92));
		topology.AddArc(0, 1, null);
		topology.AddArc(1, 4, null);
		topology.AddArc(4, 5, null);
		topology.AddArc(5, 0, null);
		topology.AddArc(1, 2, null);
		topology.AddArc(2, 3, null);
		topology.AddArc(3, 4, null);
		topology.AddRegionControlPoint(new Point(2.0, 0.46));
		topology.AddRegionControlPoint(new Point(7.0, 0.46));
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.Quad, SizeFunction.Constant, float.NaN);
		topology.SetControlPointData(1, PatchType.Quad, SizeFunction.Constant, float.NaN);
		topology.SetControlPointTINResolution(0, -1f);
		topology.SetControlPointTINResolution(1, -1f);
		SetCount(topology, 0, count);
		SetCount(topology, 1, CellsAcross);
		SetCount(topology, 2, count);
		SetCount(topology, 3, CellsAcross);
		SetCount(topology, 4, count2);
		SetCount(topology, 5, CellsAcross);
		SetCount(topology, 6, count2);
		return topology;
	}

	public override IReadOnlyList<MeasuredVelocitySample> GetMeasuredVelocitySamples()
	{
		double[] coefficients = new double[40]
		{
			4.3048, 4.3048, 4.3048, 4.3048, 4.3048, 4.3048, 4.3048, 4.3048, 4.3048, 4.6096,
			4.6096, 4.6096, 4.6096, 4.6096, 4.6096, 4.6096, 4.6096, 4.6096, 4.6096, 4.6096,
			4.9144, 4.9144, 4.9144, 4.9144, 4.9144, 4.9144, 4.9144, 4.9144, 4.9144, 4.9144,
			5.2192, 5.2192, 5.2192, 5.2192, 5.2192, 5.2192, 5.2192, 5.2192, 5.2192, 5.2192
		};
		double[] coefficientsArray = new double[40]
		{
			0.0669, 0.1033, 0.1339, 0.1683, 0.2293, 0.269, 0.3388, 0.4228, 0.5754, 0.0645,
			0.1016, 0.1381, 0.1686, 0.1824, 0.1937, 0.227, 0.265, 0.3404, 0.4201, 0.5759,
			0.0696, 0.1009, 0.1357, 0.1829, 0.1908, 0.226, 0.267, 0.3442, 0.4212, 0.5792,
			0.0673, 0.0936, 0.1305, 0.1692, 0.181, 0.2317, 0.2731, 0.3392, 0.4218, 0.579
		};
		double[] doubleValues = new double[40]
		{
			-0.1228, -0.0985, -0.0979, -0.0255, 0.0978, 0.2873, 0.3734, 0.3736, 0.3863, -0.1174,
			-0.1178, -0.0942, -0.0218, 0.0443, 0.0589, 0.1472, 0.232, 0.401, 0.3891, 0.4079,
			-0.1246, -0.1036, -0.0895, 0.0617, 0.0801, 0.1491, 0.222, 0.372, 0.3889, 0.3954,
			-0.1213, -0.1082, -0.1022, 0.0527, 0.0955, 0.1908, 0.2729, 0.3516, 0.3788, 0.3892
		};
		List<MeasuredVelocitySample> measuredVelocitySamples = new List<MeasuredVelocitySample>(coefficients.Length);
		for (int i = 0; i < coefficients.Length; i++)
		{
			measuredVelocitySamples.Add(new MeasuredVelocitySample(new Point(coefficients[i], coefficientsArray[i]), -1.0, new Vector(doubleValues[i], 0.0)));
		}
		return measuredVelocitySamples;
	}

	private static void SetCount(Topology topo, int arcIdx, int count)
	{
		topo.SetArcData(arcIdx, LinearSizeFunction.CountWithBias, float.NaN, count, 1f);
	}
}
