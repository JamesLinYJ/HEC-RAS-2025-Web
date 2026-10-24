using System;
using System.Collections.Generic;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Synthetics.SyntheticParams;
using Utility.Logging;
using Utility.Units;

namespace Ras.Synthetics;

public class RainOnMesh3PlaneBasinParams : BaseParams
{
	public enum HyetographCase
	{
		C1_317mm_45s,
		C2_320mm_25s_4s_25s,
		C3_328mm_25s_7s_25s
	}

	public const double OutletY = 0.0;

	public const double TopY = 2.5;

	public const double HalfWidth = 1.0;

	public double CellSize = 0.05;

	public static readonly Point RegionControlPoint = new Point(101.0 / 320.0, 1.818125);

	public const double PlaneSlope = 0.05;

	public const double ChannelSlope = 0.05;

	public const double UpperPlaneBreakY = 1.5;

	public double OutflowFrictionSlope = 0.05;

	private Mesh _mesh;

	public const double OutletBcY = -0.004;

	private static readonly (double TimeSeconds, double Flow)[] _obs1 = new (double, double)[62]
	{
		(0.0, 0.0),
		(1.39, 5E-07),
		(3.86, 2.9E-06),
		(6.08, 1.15E-05),
		(7.92, 1.84E-05),
		(9.44, 2.4E-05),
		(10.35, 3.37E-05),
		(10.95, 5.28E-05),
		(11.13, 7.65E-05),
		(11.31, 0.0001076),
		(12.44, 0.0001393),
		(13.15, 0.0001628),
		(14.26, 0.000185),
		(15.23, 0.0002083),
		(16.52, 0.0002302),
		(17.62, 0.0002571),
		(19.52, 0.0002796),
		(21.78, 0.0003009),
		(23.71, 0.0003226),
		(26.5, 0.0003412),
		(28.72, 0.0003563),
		(31.17, 0.0003662),
		(34.57, 0.0003765),
		(37.0, 0.000385),
		(39.97, 0.0003876),
		(43.01, 0.0003915),
		(45.03, 0.0003934),
		(46.72, 0.0003888),
		(48.26, 0.0003766),
		(49.75, 0.0003655),
		(50.67, 0.000355),
		(51.62, 0.0003377),
		(52.78, 0.0003108),
		(52.93, 0.0002982),
		(53.7, 0.0002835),
		(54.42, 0.0002687),
		(54.79, 0.0002571),
		(55.6, 0.0002437),
		(56.53, 0.0002243),
		(57.64, 0.0002114),
		(58.38, 0.0001926),
		(59.36, 0.0001763),
		(59.92, 0.0001648),
		(60.65, 0.0001531),
		(61.78, 0.0001395),
		(62.71, 0.0001275),
		(64.26, 0.0001142),
		(65.94, 9.91E-05),
		(68.06, 8.4E-05),
		(70.29, 7.07E-05),
		(72.86, 5.6E-05),
		(75.71, 4.47E-05),
		(78.89, 3.43E-05),
		(84.53, 2.5E-05),
		(88.84, 1.81E-05),
		(93.94, 1.39E-05),
		(98.22, 9.9E-06),
		(102.73, 8.7E-06),
		(107.23, 7.4E-06),
		(111.72, 5.9E-06),
		(117.37, 3.8E-06),
		(119.41, 2.7E-06)
	};

	private static readonly (double TimeSeconds, double Flow)[] _obs2 = new (double, double)[61]
	{
		(0.0, 0.0),
		(1.12, 2.5E-06),
		(2.97, 3.4E-06),
		(5.19, 8E-06),
		(7.47, 1.71E-05),
		(9.51, 2.55E-05),
		(10.58, 4.53E-05),
		(10.95, 7.4E-05),
		(11.13, 9.91E-05),
		(11.89, 0.0001275),
		(12.43, 0.0001486),
		(14.12, 0.0001827),
		(14.84, 0.0002126),
		(15.6, 0.0002419),
		(17.24, 0.0002732),
		(18.6, 0.0002933),
		(20.22, 0.0003135),
		(21.93, 0.0003298),
		(23.99, 0.0003479),
		(25.68, 0.0003596),
		(27.31, 0.0003644),
		(28.82, 0.0003571),
		(30.1, 0.0003462),
		(31.27, 0.0003347),
		(32.55, 0.0003211),
		(33.69, 0.000304),
		(34.61, 0.0002915),
		(36.29, 0.0002871),
		(37.57, 0.0002937),
		(38.31, 0.000307),
		(38.85, 0.0003229),
		(40.16, 0.0003416),
		(42.04, 0.0003542),
		(43.51, 0.0003705),
		(46.16, 0.0003838),
		(48.2, 0.0003936),
		(51.17, 0.0004078),
		(53.42, 0.0004132),
		(56.2, 0.0004044),
		(57.71, 0.0003899),
		(58.81, 0.0003769),
		(60.14, 0.0003547),
		(60.89, 0.0003292),
		(61.81, 0.0003078),
		(63.14, 0.0002797),
		(64.47, 0.0002507),
		(65.93, 0.0002215),
		(67.85, 0.0001832),
		(69.9, 0.0001532),
		(71.95, 0.0001252),
		(74.89, 0.0001008),
		(78.6, 7.91E-05),
		(81.82, 6.27E-05),
		(83.86, 5.16E-05),
		(86.84, 4.18E-05),
		(90.62, 3.29E-05),
		(95.77, 2.47E-05),
		(101.21, 1.67E-05),
		(107.0, 1.02E-05),
		(111.65, 7.7E-06),
		(116.48, 6.4E-06)
	};

	private static readonly (double TimeSeconds, double Flow)[] _obs3 = new (double, double)[81]
	{
		(0.0, 0.0),
		(1.33, 5E-07),
		(2.23, 4.2E-06),
		(3.8, 6.5E-06),
		(5.92, 1.2E-05),
		(8.05, 2.36E-05),
		(9.77, 3.04E-05),
		(10.54, 5.3E-05),
		(10.68, 7.65E-05),
		(10.73, 9.74E-05),
		(11.08, 0.0001159),
		(11.86, 0.0001355),
		(13.0, 0.0001598),
		(13.97, 0.0001857),
		(14.72, 0.0002098),
		(15.49, 0.0002352),
		(17.25, 0.0002639),
		(18.79, 0.0002829),
		(20.5, 0.0003055),
		(22.58, 0.0003309),
		(24.01, 0.0003458),
		(25.46, 0.0003516),
		(26.86, 0.0003499),
		(28.4, 0.000345),
		(29.78, 0.0003368),
		(30.51, 0.0003272),
		(30.97, 0.0003188),
		(31.71, 0.0003049),
		(32.48, 0.0002949),
		(32.56, 0.0002811),
		(32.9, 0.000269),
		(34.24, 0.0002582),
		(35.06, 0.0002467),
		(36.02, 0.0002354),
		(36.98, 0.0002248),
		(38.52, 0.0002192),
		(39.87, 0.0002243),
		(41.25, 0.0002363),
		(41.76, 0.0002575),
		(42.37, 0.0002743),
		(42.7, 0.00029),
		(43.92, 0.0003057),
		(45.25, 0.0003188),
		(46.58, 0.0003344),
		(48.11, 0.000352),
		(50.47, 0.0003658),
		(52.39, 0.000375),
		(54.33, 0.0003821),
		(56.81, 0.0003875),
		(57.61, 0.000391),
		(59.57, 0.0003842),
		(60.76, 0.0003706),
		(61.88, 0.0003582),
		(63.04, 0.0003452),
		(63.46, 0.0003294),
		(64.25, 0.0003127),
		(64.59, 0.0002992),
		(65.44, 0.0002772),
		(66.38, 0.0002579),
		(66.96, 0.000245),
		(68.14, 0.0002235),
		(69.12, 0.0002023),
		(70.1, 0.0001835),
		(72.24, 0.0001611),
		(73.36, 0.0001417),
		(75.14, 0.0001239),
		(76.85, 0.0001072),
		(79.16, 8.97E-05),
		(81.13, 7.66E-05),
		(83.67, 6.33E-05),
		(86.33, 5.28E-05),
		(89.06, 4.5E-05),
		(91.39, 3.72E-05),
		(94.3, 2.95E-05),
		(97.56, 2.33E-05),
		(102.24, 1.69E-05),
		(107.07, 1.36E-05),
		(110.14, 1.06E-05),
		(114.0, 8.2E-06),
		(117.49, 6.1E-06),
		(119.81, 4.3E-06)
	};

	private static readonly Point Node0 = new Point(-1.0, 0.0);

	private static readonly Point Node1 = new Point(-0.4286, 1.8617);

	private static readonly Point Node2 = new Point(-0.37, 1.8419);

	private static readonly Point Node3 = new Point(0.0845, 1.0876);

	private static readonly Point Node4 = new Point(0.1406, 1.1097);

	private static readonly Point[] _perimeterNode0ToNode3 = new Point[7]
	{
		new Point(-0.68340742, 0.0),
		new Point(-0.67157754, 0.08643264),
		new Point(-0.6708943, 0.09142459),
		new Point(-0.5621123, 0.88621879),
		new Point(-0.55472813, 0.94016972),
		new Point(-0.52440768, 1.16170012),
		new Point(-0.43752643, 1.7964808)
	};

	private static readonly Point[] _perimeterNode4ToNode5 = new Point[4]
	{
		new Point(-0.62519637, 0.0),
		new Point(-0.31538198, 0.0),
		new Point(0.25568684, 0.0),
		new Point(0.67249819, 0.0)
	};

	private static readonly Point[] _perimeterNode6ToNode0 = new Point[4]
	{
		new Point(0.73010925, 0.0),
		new Point(1.0, 0.0),
		new Point(1.0, 2.5),
		new Point(-1.0, 2.5)
	};

	public override float NValue { get; set; } = 0.009f;

	public override UnitSystem Units => UnitSystem.SI;

	public override Extent SystemExtent => CreateMesh().Extent;

	public static RainOnMesh3PlaneBasinParams Default()
	{
		return new RainOnMesh3PlaneBasinParams
		{
			OutflowFrictionSlope = 0.05,
			SolveDt = 0.05,
			SolveDuration = 120.0,
			ReportFrequency = 1
		};
	}

	public override Mesh CreateMesh()
	{
		if (_mesh == null)
		{
			Topology topology = MakeTopology(CellSize);
			topology.Complete();
			OperationResult operationResult = topology.BuildConceptualMesh(out var _, out _mesh);
			if (!operationResult.Result || _mesh == null)
			{
				throw new Exception("Rain-on-mesh basin mesh generation failed: " + operationResult.GetConcatenatedMessages(";"));
			}
		}
		return _mesh;
	}

	public static Topology MakeTopology(double cellSize)
	{
		Topology topology = new Topology();
		topology.AddNode(Node0);
		topology.AddNode(Node1);
		topology.AddNode(Node2);
		topology.AddNode(Node3);
		topology.AddNode(Node4);
		topology.AddArc(0, 1, new List<Point>(_perimeterNode0ToNode3));
		topology.AddArc(1, 2);
		topology.AddArc(2, 3, new List<Point>(_perimeterNode4ToNode5));
		topology.AddArc(3, 4);
		topology.AddArc(4, 0, new List<Point>(_perimeterNode6ToNode0));
		topology.AddRegionControlPoint(RegionControlPoint);
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.TrianglePaver, SizeFunction.Constant, (float)cellSize);
		topology.SetControlPointRecipeString(0, "# None");
		topology.SetControlPointTINResolution(0, 1f);
		for (int i = 0; i < 5; i++)
		{
			topology.SetArcData(i, LinearSizeFunction.Unset, float.NaN, 8, 1f);
		}
		return topology;
	}

	public override float TerrainElevation(Point pt)
	{
		return (float)Math.Max(0.05 * Math.Abs(pt.X), 0.05 * (pt.Y - 1.5));
	}

	public override Func<Point, float> InitialStageFunction()
	{
		return null;
	}

	public override void InitPlanOptions(Plan pl)
	{
		base.InitPlanOptions(pl);
		pl.SolverType = SolverType.CPU;
	}

	public override void InitSolverControl(SolverControl sc)
	{
		sc.dtMinMicros = 100L;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.NormalDepth(bc, OutletBcTransect(), OutflowFrictionSlope);
		boundaryConditionLine.Name = "Outlet";
		return new List<BoundaryConditionLine> { boundaryConditionLine };
	}

	public static Polyline OutletBcTransect()
	{
		return new Polyline(new Point[2]
		{
			new Point(0.256, -0.004),
			new Point(-0.315, -0.004)
		});
	}

	public static string BoundaryConditionName(HyetographCase hyetographCase)
	{
		return hyetographCase switch
		{
			HyetographCase.C1_317mm_45s => "C1 317mm - 45s", 
			HyetographCase.C2_320mm_25s_4s_25s => "C2 320mm - 25s 4s 25s", 
			HyetographCase.C3_328mm_25s_7s_25s => "C3 328mm - 25s 7s 25s", 
			_ => throw new ArgumentOutOfRangeException("hyetographCase"), 
		};
	}

	public void ImportPrecipitation(Ras.Layers.BoundaryCondition bc, HyetographCase hyetographCase, DateTime startTime)
	{
		(double t, double mmPerHr)[] steps = HyetographSteps(hyetographCase);
		int count = (int)Math.Ceiling(SolveDuration) + 1;
		MultiBandFunctionalResampler<float> resampler = new MultiBandFunctionalResampler<float>(count, SystemExtent, -9999f, (int band, Point pt) => RateAt(band));
		List<DateTime> dateTimes = new List<DateTime>(count);
		for (int ordinal = 0; ordinal < count; ordinal++)
		{
			dateTimes.Add(startTime.AddSeconds(ordinal));
		}
		RasterDefinition raster = RasterDefinition.ComputeBoundingRaster(SystemExtent, 1);
		ITimeSeriesRaster<float> timed = resampler.AsTimeSeriesRaster(dateTimes, raster);
		bc.Precipitation.ImportRaster(timed, null, "per-avg", PeriodAveragePrecipitation.MillimeterPerHour.Name);
		bc.Precipitation.SetUseImportedRaster();
		bc.StartTime = startTime;
		bc.EndTime = dateTimes[dateTimes.Count - 1];
		float RateAt(int second)
		{
			double computed = 0.0;
			(double, double)[] stepsArray = steps;
			for (int i = 0; i < stepsArray.Length; i++)
			{
				var (localValue, localValue2) = stepsArray[i];
				if ((double)second < localValue)
				{
					break;
				}
				computed = localValue2;
			}
			return (float)computed;
		}
	}

	public static void GetHyetograph(HyetographCase hyetographCase, double durationSeconds, out double[] times, out float[] rates)
	{
		(double, double)[] hyetographSteps = HyetographSteps(hyetographCase);
		times = new double[hyetographSteps.Length + 1];
		rates = new float[hyetographSteps.Length + 1];
		for (int i = 0; i < hyetographSteps.Length; i++)
		{
			times[i] = hyetographSteps[i].Item1;
			rates[i] = (float)MmPerHrToMetersPerSecond(hyetographSteps[i].Item2);
		}
		times[^1] = Math.Max(durationSeconds, hyetographSteps[^1].Item1);
		rates[^1] = 0f;
	}

	public static (double TimeSeconds, double MmPerHr)[] HyetographSteps(HyetographCase hyetographCase)
	{
		return hyetographCase switch
		{
			HyetographCase.C1_317mm_45s => new (double, double)[2]
			{
				(0.0, 317.0),
				(45.0, 0.0)
			}, 
			HyetographCase.C2_320mm_25s_4s_25s => new (double, double)[4]
			{
				(0.0, 320.0),
				(25.0, 0.0),
				(29.0, 320.0),
				(54.0, 0.0)
			}, 
			HyetographCase.C3_328mm_25s_7s_25s => new (double, double)[4]
			{
				(0.0, 328.0),
				(25.0, 0.0),
				(32.0, 328.0),
				(57.0, 0.0)
			}, 
			_ => throw new ArgumentOutOfRangeException("hyetographCase"), 
		};
	}

	public static double MmPerHrToMetersPerSecond(double mmPerHr)
	{
		return mmPerHr / 1000.0 / 3600.0;
	}

	public static (double TimeSeconds, double Flow)[] GetObservedOutflow(HyetographCase hyetographCase)
	{
		return hyetographCase switch
		{
			HyetographCase.C1_317mm_45s => _obs1, 
			HyetographCase.C2_320mm_25s_4s_25s => _obs2, 
			HyetographCase.C3_328mm_25s_7s_25s => _obs3, 
			_ => throw new ArgumentOutOfRangeException("hyetographCase"), 
		};
	}
}
