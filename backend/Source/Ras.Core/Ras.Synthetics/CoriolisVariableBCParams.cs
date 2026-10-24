using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Engine;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class CoriolisVariableBCParams : BasicRectangleParams
{
	private const int BoundaryRampSampleCount = 20;

	public double DownstreamStage = 100.0;

	public double UpstreamFlow = 500000.0;

	public double RampSeconds = 172800.0;

	public double Velocity = 1.0;

	public double FCoriolis = 0.0001;

	public CoriolisVariableBCParams()
	{
		CellsTall = 5;
		CellsWide = 25;
		CellSize = 1000.0;
		NValue = 0.01f;
	}

	public override Func<Point, double, float> AnalyticalStageFunction()
	{
		return (Point pt, double time) =>
		{
			double fCoriolisTimesVelocityOverGravSI = FCoriolis * Velocity / SolverControl.GravSI;
			fCoriolisTimesVelocityOverGravSI *= BaseParams.RampFactor(time, RampSeconds);
			return (float)(DownstreamStage - fCoriolisTimesVelocityOverGravSI * (pt.Y - (double)CellsTall * CellSize / 2.0));
		};
	}

	public override Func<Point, float> InitialStageFunction()
	{
		Func<Point, double, float> anaFn = AnalyticalStageFunction();
		return (Point pt) => anaFn(pt, 0.0);
	}

	public override void InitSolverControl(SolverControl sc)
	{
		base.InitSolverControl(sc);
		sc.variableDt = false;
		sc.stepType = SolverControl.StepType.FWDBACK;
		sc.dtMinMicros = 10000000L;
		sc.fCoriolis = FCoriolis;
	}

	public override void InitPlanOptions(Plan pl)
	{
		base.InitPlanOptions(pl);
		double scaledFCoriolis = FCoriolis / 0.0001454441043328608;
		if (Math.Abs(scaledFCoriolis) > 1.0)
		{
			throw new ArgumentOutOfRangeException("FCoriolis", "Coriolis frequency cannot be represented by a latitude.");
		}
		pl.Latitude = Math.Asin(scaledFCoriolis) * 180.0 / Math.PI;
	}

	public override List<BoundaryConditionLine> GetBCLines(Ras.Layers.BoundaryCondition bc)
	{
		Mesh mesh = CreateMesh();
		Extent extent = mesh.Extent;
		double cellSizeXCellSizeTerm = 0.1 * ((CellSizeX > 0.0) ? CellSizeX : CellSize);
		Polyline polyline = Polyline.FromSegment(new Segment(new Point(extent.MinX - cellSizeXCellSizeTerm, extent.MinY), new Point(extent.MinX - cellSizeXCellSizeTerm, extent.MaxY)));
		List<ProfileFaceRange> faces = mesh.PerimeterFacesAlongPolyline(polyline, out var magnetizedPline);
		Polyline polyline2 = Polyline.FromSegment(new Segment(new Point(extent.MaxX + cellSizeXCellSizeTerm, extent.MaxY), new Point(extent.MaxX + cellSizeXCellSizeTerm, extent.MinY)));
		List<ProfileFaceRange> faces2 = mesh.PerimeterFacesAlongPolyline(polyline2, out magnetizedPline);
		TimeSpan interval = BoundaryInterval();
		double[] timesteps = BoundaryTimesteps(interval);
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, polyline)
		{
			Name = "Upstream"
		};
		boundaryConditionLine.Add(BuildFlowPayload(mesh, faces, interval, timesteps));
		BoundaryConditionLine boundaryConditionLine2 = new BoundaryConditionLine(bc, polyline2)
		{
			Name = "Downstream"
		};
		boundaryConditionLine2.Add(BuildStagePayload(mesh, faces2, interval, timesteps));
		int ordinal = 2;
		List<BoundaryConditionLine> boundaryConditionLines = new List<BoundaryConditionLine>(ordinal);
		CollectionsMarshal.SetCount(boundaryConditionLines, ordinal);
		Span<BoundaryConditionLine> boundaryConditionLinesSpan = CollectionsMarshal.AsSpan(boundaryConditionLines);
		int ordinal2 = 0;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine;
		ordinal2++;
		boundaryConditionLinesSpan[ordinal2] = boundaryConditionLine2;
		return boundaryConditionLines;
	}

	private TimeSpan BoundaryInterval()
	{
		TimeSpan timeSpan = TimeSpan.FromSeconds((RampSeconds > 0.0) ? (RampSeconds / 20.0) : SolveDuration);
		if (timeSpan <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException("SolveDuration", "Boundary condition duration must be positive.");
		}
		return timeSpan;
	}

	private double[] BoundaryTimesteps(TimeSpan interval)
	{
		int count = checked((int)Math.Ceiling(SolveDuration / interval.TotalSeconds) + 1);
		double[] doubleValues = new double[count];
		for (int i = 0; i < count; i++)
		{
			doubleValues[i] = (double)i * interval.TotalSeconds;
		}
		return doubleValues;
	}

	private SpatiallyVariableTimeSeriesPayload BuildFlowPayload(Mesh mesh, IReadOnlyList<ProfileFaceRange> faces, TimeSpan interval, double[] timesteps)
	{
		double[] stations = FaceStations(faces);
		List<Memory<double>> newValueRows = NewValueRows(timesteps.Length, faces.Count);
		Func<Point, double, float> func = AnalyticalStageFunction();
		double rampSecondsSolveDurationTerm = Math.Max(RampSeconds, SolveDuration);
		if (RampSeconds == 0.0)
		{
			double upstreamFlowOffsetOverCount = (0.0 - UpstreamFlow) / (double)faces.Count;
			foreach (Memory<double> newValueRow in newValueRows)
			{
				newValueRow.Span.Fill(upstreamFlowOffsetOverCount);
			}
			return NewSpatialPayload("Flow", "Variable Flow", interval, stations, newValueRows);
		}
		for (int i = 0; i < faces.Count; i++)
		{
			int face = faces[i].Face;
			double faceSimpleLength = mesh.FaceSimpleLength(face);
			double func2 = func(mesh.FaceMidPoint(face), rampSecondsSolveDurationTerm);
			double faceSimpleLengthVelocityTerm = (0.0 - faceSimpleLength) * Velocity * func2;
			for (int j = 0; j < timesteps.Length; j++)
			{
				newValueRows[j].Span[i] = faceSimpleLengthVelocityTerm * BaseParams.RampFactor(timesteps[j], RampSeconds);
			}
		}
		return NewSpatialPayload("Flow", "Variable Flow", interval, stations, newValueRows);
	}

	private SpatiallyVariableTimeSeriesPayload BuildStagePayload(Mesh mesh, IReadOnlyList<ProfileFaceRange> faces, TimeSpan interval, double[] timesteps)
	{
		double[] stations = FaceStations(faces);
		List<Memory<double>> newValueRows = NewValueRows(timesteps.Length, faces.Count);
		Func<Point, double, float> func = AnalyticalStageFunction();
		for (int i = 0; i < faces.Count; i++)
		{
			Point faceMidPoint = mesh.FaceMidPoint(faces[i].Face);
			for (int j = 0; j < timesteps.Length; j++)
			{
				newValueRows[j].Span[i] = func(faceMidPoint, timesteps[j]);
			}
		}
		return NewSpatialPayload("Stage", "Variable Stage", interval, stations, newValueRows);
	}

	private static SpatiallyVariableTimeSeriesPayload NewSpatialPayload(string variable, string name, TimeSpan interval, double[] stations, List<Memory<double>> values)
	{
		return new SpatiallyVariableTimeSeriesPayload(variable, new RegularTimeSeriesDefinition(interval), stations, values)
		{
			Name = name
		};
	}

	private static double[] FaceStations(IReadOnlyList<ProfileFaceRange> faces)
	{
		return faces.Select((ProfileFaceRange face) => 0.5 * (face.StationStart + face.StationEnd)).ToArray();
	}

	private static List<Memory<double>> NewValueRows(int timeCount, int faceCount)
	{
		List<Memory<double>> doubleSpanList = new List<Memory<double>>(timeCount);
		for (int i = 0; i < timeCount; i++)
		{
			doubleSpanList.Add(new double[faceCount]);
		}
		return doubleSpanList;
	}
}
