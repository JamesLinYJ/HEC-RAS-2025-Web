using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;
using Utility.Units;

namespace Ras.Synthetics;

public class Bend180Params : BaseParams
{
	public const double ChannelWidth = 0.8;

	public const double InnerRadius = 0.4;

	public const double OuterRadius = 1.2000000000000002;

	public const double CenterlineRadius = 0.8;

	public const double BottomStraightLength = 6.0;

	public const double TopStraightLength = 3.0;

	public const double JunctionX = 6.0;

	public const double TopStraightLeftX = 3.0;

	public const double BendCenterY = 1.2;

	public static readonly Point BendCenter = new Point(6.0, 1.2);

	public const int CellsAcross = 20;

	public double UpstreamFlow = 0.0123;

	public double DownstreamStage = 0.057;

	public double RampSeconds = 100.0;

	private Mesh _mesh;

	private static readonly int[] _velocityGroupSizes = new int[6] { 5, 9, 9, 9, 9, 9 };

	public override UnitSystem Units => UnitSystem.SI;

	public override Extent SystemExtent => CreateMesh().Extent;

	public static Bend180Params Default()
	{
		return new Bend180Params
		{
			NValue = 0.01f,
			UpstreamFlow = 0.0123,
			DownstreamStage = 0.057,
			RampSeconds = 100.0,
			SolveDt = 0.01,
			SolveDuration = 300.0,
			ReportFrequency = 1
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
		sc.dtMinMicros = 1000L;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		double computed = 0.02;
		double y = BendCenter.Y - 1.2000000000000002;
		double y2 = BendCenter.Y - 0.4;
		Polyline pline = new Polyline
		{
			{
				0.0 - computed,
				y
			},
			{
				0.0 - computed,
				y2
			}
		};
		double y3 = BendCenter.Y + 0.4;
		double y4 = BendCenter.Y + 1.2000000000000002;
		Polyline pline2 = new Polyline
		{
			{
				3.0 - computed,
				y3
			},
			{
				3.0 - computed,
				y4
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
		TimeSpan ts = TimeSpan.FromSeconds(1L);
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
		double computed = 0.04;
		double computed2 = 2.0 * computed;
		int count = (int)Math.Round(6.0 / computed2);
		int count2 = (int)Math.Round(3.0 / computed2);
		int count3 = (int)Math.Round(3.769911184307752 / computed2);
		double y = BendCenter.Y - 1.2000000000000002;
		double y2 = BendCenter.Y - 0.4;
		double y3 = BendCenter.Y + 0.4;
		double y4 = BendCenter.Y + 1.2000000000000002;
		Topology topology = new Topology();
		topology.AddNode(new Point(0.0, y));
		topology.AddNode(new Point(6.0, y));
		topology.AddNode(new Point(6.0, y2));
		topology.AddNode(new Point(0.0, y2));
		topology.AddNode(new Point(6.0, y3));
		topology.AddNode(new Point(6.0, y4));
		topology.AddNode(new Point(3.0, y4));
		topology.AddNode(new Point(3.0, y3));
		List<Point> internalVertexes = ArcInteriorPoints(BendCenter, 1.2000000000000002, -Math.PI / 2.0, Math.PI / 2.0, 120);
		List<Point> internalVertexes2 = ArcInteriorPoints(BendCenter, 0.4, -Math.PI / 2.0, Math.PI / 2.0, 120);
		topology.AddArc(0, 1, null);
		topology.AddArc(1, 2, null);
		topology.AddArc(2, 3, null);
		topology.AddArc(3, 0, null);
		topology.AddArc(1, 5, internalVertexes);
		topology.AddArc(2, 4, internalVertexes2);
		topology.AddArc(4, 5, null);
		topology.AddArc(5, 6, null);
		topology.AddArc(6, 7, null);
		topology.AddArc(7, 4, null);
		topology.AddRegionControlPoint(new Point(3.0, BendCenter.Y - 0.8));
		topology.AddRegionControlPoint(BendCenter + new Vector(0.8, 0.0));
		topology.AddRegionControlPoint(new Point(4.5, BendCenter.Y + 0.8));
		topology.EnsureMeshMetadataColumns();
		topology.SetControlPointData(0, PatchType.Quad, SizeFunction.Constant, float.NaN);
		topology.SetControlPointData(1, PatchType.Quad, SizeFunction.Constant, float.NaN);
		topology.SetControlPointData(2, PatchType.Quad, SizeFunction.Constant, float.NaN);
		topology.SetControlPointTINResolution(0, -1f);
		topology.SetControlPointTINResolution(1, -1f);
		topology.SetControlPointTINResolution(2, -1f);
		SetCount(topology, 0, count);
		SetCount(topology, 1, 20);
		SetCount(topology, 2, count);
		SetCount(topology, 3, 20);
		SetCount(topology, 4, count3);
		SetCount(topology, 5, count3);
		SetCount(topology, 6, 20);
		SetCount(topology, 7, count2);
		SetCount(topology, 8, 20);
		SetCount(topology, 9, count2);
		return topology;
	}

	private static void SetCount(Topology topo, int arcIdx, int count)
	{
		topo.SetArcData(arcIdx, LinearSizeFunction.CountWithBias, float.NaN, count, 1f);
	}

	private static List<Point> ArcInteriorPoints(Point center, double radius, double startAngle, double endAngle, int samples)
	{
		List<Point> points = new List<Point>(samples - 1);
		for (int i = 1; i < samples; i++)
		{
			double startAngleSamplesTerm = startAngle + (double)i / (double)samples * (endAngle - startAngle);
			points.Add(new Point(center.X + radius * Math.Cos(startAngleSamplesTerm), center.Y + radius * Math.Sin(startAngleSamplesTerm)));
		}
		return points;
	}

	public double[] GetMeasuredVelocitySamplesRadius()
	{
		return new double[50]
		{
			0.430676, 0.6001728, 0.7997304, 0.9919256, 1.1669904, 0.4307856, 0.5040384, 0.600528, 0.7076776, 0.7987016,
			0.895464, 0.9916824, 1.0947312, 1.1657976, 0.4304768, 0.5035288, 0.5999672, 0.7064096, 0.79802, 0.8948056,
			0.9915752, 1.0942824, 1.1659616, 0.4300136, 0.5029968, 0.5988768, 0.7046488, 0.7966296, 0.89414, 0.99108,
			1.0951792, 1.167084, 0.4311672, 0.5036448, 0.5989096, 0.7046256, 0.7968872, 0.8932776, 0.9904728, 1.0945592,
			1.1662312, 0.431064, 0.5029784, 0.5987664, 0.7043456, 0.7960816, 0.893252, 0.9898728, 1.094644, 1.166036
		};
	}

	public override IReadOnlyList<MeasuredVelocityProfile> GetMeasuredVelocityProfiles()
	{
		double[] coefficients = new double[50]
		{
			6.0, 6.0, 6.0, 6.0, 6.0, 6.247088469, 6.289104549, 6.34444871, 6.405907196, 6.458116417,
			6.51361705, 6.568805657, 6.62791202, 6.668674033, 6.390144476, 6.456352072, 6.543754945, 6.640224521, 6.72325174, 6.810969283,
			6.898672325, 6.99175666, 7.056720077, 6.423480727, 6.495355148, 6.589778516, 6.693943601, 6.784527006, 6.880556004, 6.976023268,
			7.078540967, 7.149353372, 6.259482898, 6.303101007, 6.360432795, 6.424054272, 6.479578689, 6.53758788, 6.596081411, 6.65872217,
			6.701855457, 5.954941543, 5.947424441, 5.937411868, 5.926375837, 5.916786814, 5.906629741, 5.896530117, 5.885578545, 5.878116049
		};
		double[] coefficientsArray = new double[50]
		{
			0.769324, 0.5998272, 0.4002696, 0.2080744, 0.0330096, 0.847121095, 0.787115914, 0.708076261, 0.620304447, 0.545741952,
			0.466478834, 0.387661335, 0.3032487, 0.245034513, 1.018072643, 0.987199534, 0.946442905, 0.901458403, 0.862742175, 0.821838813,
			0.780942213, 0.737536274, 0.707243335, 1.274671078, 1.287344478, 1.303993865, 1.32236098, 1.338333278, 1.355265782, 1.372099236,
			1.390175872, 1.40266201, 1.544345437, 1.602228622, 1.678310474, 1.762739025, 1.836422415, 1.913403212, 1.99102675, 2.074153845,
			2.131393649, 1.628702586, 1.700223032, 1.795486295, 1.900487121, 1.991720582, 2.088358672, 2.184450173, 2.288647426, 2.359648333
		};
		double[] doubleValues = new double[50]
		{
			0.305749315, 0.322658435, 0.30598437, 0.23737481, 0.152971515, 0.31883411, 0.292327481, 0.267070119, 0.236013157, 0.216971313,
			0.191299772, 0.171844833, 0.140786785, 0.115522912, 0.172628424, 0.16546037, 0.139295474, 0.119676393, 0.100055296, 0.093523703,
			0.082769831, 0.073494613, 0.059992972, -0.064194725, -0.061770331, -0.053598858, -0.048044082, -0.045710986, -0.040328773, -0.037560956,
			-0.03479475, -0.029319767, -0.245957695, -0.246061821, -0.246609753, -0.23278659, -0.220588175, -0.200997231, -0.194971462, -0.168403858,
			-0.15535381, -0.172640742, -0.202695526, -0.260216261, -0.27302602, -0.287854835, -0.286935315, -0.303801616, -0.302889475, -0.286698385
		};
		double[] doubleValuesArray = new double[50]
		{
			0.0, 0.0, 0.0, 0.0, 0.0, 0.223250048, 0.204689906, 0.187004511, 0.165258192, 0.151924949,
			0.133949542, 0.120327047, 0.098579968, 0.080890014, 0.37020285, 0.354830909, 0.298720107, 0.256646852, 0.214569275, 0.200562229,
			0.177500475, 0.157609707, 0.128655343, 0.364066374, 0.350316956, 0.303974228, 0.272471527, 0.259239882, 0.228715835, 0.213018767,
			0.197330834, 0.166280663, 0.185342417, 0.185420882, 0.185833778, 0.175417278, 0.166225113, 0.151462277, 0.146921535, 0.126901409,
			0.117067493, -0.018145273, -0.021304158, -0.027349831, -0.028696192, -0.030254762, -0.030158117, -0.031930836, -0.031834967, -0.030133215
		};
		double[] measuredVelocitySamplesRadius = GetMeasuredVelocitySamplesRadius();
		List<MeasuredVelocityProfile> measuredVelocityProfiles = new List<MeasuredVelocityProfile>(_velocityGroupSizes.Length);
		int velocityGroupSizeSum = 0;
		for (int i = 0; i < _velocityGroupSizes.Length; i++)
		{
			int velocityGroupSize = _velocityGroupSizes[i];
			double xComponent = 0.0;
			double yComponent = 0.0;
			for (int j = velocityGroupSizeSum; j < velocityGroupSizeSum + velocityGroupSize; j++)
			{
				double bendCenterTerm = coefficients[j] - BendCenter.X;
				double bendCenterTerm2 = coefficientsArray[j] - BendCenter.Y;
				double computed = Math.Sqrt(bendCenterTerm * bendCenterTerm + bendCenterTerm2 * bendCenterTerm2);
				if (!(computed < 1E-12))
				{
					xComponent += bendCenterTerm / computed;
					yComponent += bendCenterTerm2 / computed;
				}
			}
			Vector vector = new Vector(xComponent, yComponent).UnitVector();
			Point point = new Point(BendCenter.X + 0.4 * vector.X, BendCenter.Y + 0.4 * vector.Y);
			Point point2 = new Point(BendCenter.X + 1.2000000000000002 * vector.X, BendCenter.Y + 1.2000000000000002 * vector.Y);
			Polyline line = new Polyline(new Point[2] { point, point2 });
			double[] doubleValues2 = new double[velocityGroupSize];
			Vector[] vectors = new Vector[velocityGroupSize];
			for (int k = 0; k < velocityGroupSize; k++)
			{
				int velocityGroupSizeSumTerm = velocityGroupSizeSum + k;
				doubleValues2[k] = (measuredVelocitySamplesRadius[velocityGroupSizeSumTerm] - 0.4) / 0.8;
				vectors[k] = new Vector(doubleValues[velocityGroupSizeSumTerm], doubleValuesArray[velocityGroupSizeSumTerm]);
			}
			measuredVelocityProfiles.Add(MeasuredVelocityProfile.FromFractions($"Velocity CS{i + 1}", line, doubleValues2, vectors));
			velocityGroupSizeSum += velocityGroupSize;
		}
		return measuredVelocityProfiles;
	}

	public double[] GetMeasuredStageSamplesCenterlineStation()
	{
		return new double[17]
		{
			4.0, 5.0, 5.5, 6.0, 6.5, 7.0, 8.0, 8.5, 9.0, 10.0,
			6.0, 6.5, 7.0, 8.0, 8.5, 9.0, 10.0
		};
	}

	public override IReadOnlyList<MeasuredStageProfile> GetMeasuredStageProfiles()
	{
		double[] coefficients = new double[17]
		{
			4.0, 5.0, 5.5, 6.0, 6.245740855, 6.39857354, 6.251358301, 6.0, 5.5, 4.5,
			4.0, 6.690414782, 7.119801851, 6.70619713, 6.0, 5.5, 4.5
		};
		double[] coefficientsArray = new double[17]
		{
			0.78, 0.78, 0.78, 0.78, 1.54060451, 1.332435392, 0.863519681, 1.62, 1.62, 1.62,
			0.02, 2.156936481, 1.572080388, 0.254650534, 2.42, 2.42, 2.42
		};
		double[] stages = new double[17]
		{
			0.061778, 0.060906, 0.060436, 0.055916, 0.053249, 0.053027, 0.053151, 0.054036, 0.05398, 0.0529,
			0.060616, 0.061348, 0.061431, 0.060269, 0.058113, 0.055349, 0.0529
		};
		double radius = 1.1900000000000002;
		Polyline polyline = BuildWall(0.41000000000000003);
		Polyline polyline2 = BuildWall(radius);
		double[] doubleValues = new double[coefficients.Length];
		for (int i = 0; i < coefficients.Length; i++)
		{
			Polyline polyline3 = ((i < 10) ? polyline : polyline2);
			PointOnPolyline ptOnPoly = polyline3.NearestPointOnPolyline(new Point(coefficients[i], coefficientsArray[i]));
			doubleValues[i] = polyline3.DistanceAlong(ptOnPoly);
		}
		return new _003C_003Ez__ReadOnlyArray<MeasuredStageProfile>(new MeasuredStageProfile[2]
		{
			BuildStageProfile("Inner wall", polyline, doubleValues, stages, 0, 10),
			BuildStageProfile("Outer wall", polyline2, doubleValues, stages, 10, doubleValues.Length - 10)
		});
	}

	private static Polyline BuildWall(double radius)
	{
		double y = BendCenter.Y - radius;
		double y2 = BendCenter.Y + radius;
		List<Point> points = new List<Point>();
		points.Add(new Point(4.0, y));
		points.Add(new Point(6.0, y));
		points.AddRange(ArcInteriorPoints(BendCenter, radius, -Math.PI / 2.0, Math.PI / 2.0, 120));
		points.Add(new Point(6.0, y2));
		points.Add(new Point(4.5, y2));
		return new Polyline(points);
	}

	private static MeasuredStageProfile BuildStageProfile(string name, Polyline wall, double[] stations, double[] stages, int start, int count)
	{
		double length = wall.Length;
		double[] doubleValues = new double[count];
		double[] doubleValuesArray = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = stations[start + i] / length;
			doubleValuesArray[i] = stages[start + i];
		}
		return MeasuredStageProfile.FromFractions(name, wall, doubleValues, doubleValuesArray);
	}
}
