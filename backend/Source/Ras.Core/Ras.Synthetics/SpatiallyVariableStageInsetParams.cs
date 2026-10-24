using System;
using System.Collections.Generic;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Ras.Synthetics.SyntheticParams;

namespace Ras.Synthetics;

public class SpatiallyVariableStageInsetParams : SpatiallyVariableStageVelocityBaseParams
{
	private double[] _westStations;

	private List<Memory<double>> _westValues;

	public static SpatiallyVariableStageInsetParams Create()
	{
		return new SpatiallyVariableStageInsetParams();
	}

	public override Mesh CreateMesh()
	{
		return CreateDumbbellMesh(100.0, includeLeftInletStack: false);
	}

	public void SetWestStageSeries(double[] stations, List<Memory<double>> values)
	{
		ValidateWestStageSeries(stations, values);
		_westStations = stations;
		_westValues = values;
	}

	public override List<BoundaryConditionLine> GetBCLines(BoundaryCondition bc)
	{
		Extent extent = CreateMesh().Extent;
		Polyline pl = new Polyline(new Point[2]
		{
			new Point(extent.MinX, 1.0),
			new Point(extent.MinX, 499.0)
		});
		if (_westStations == null || _westValues == null)
		{
			throw new InvalidOperationException("West spatially-variable stage series is required. Call SetWestStageSeries(...) before building inset boundary conditions.");
		}
		ValidateWestStageSeries(_westStations, _westValues);
		SpatiallyVariableTimeSeriesPayload payload = new SpatiallyVariableTimeSeriesPayload("Stage", new RegularTimeSeriesDefinition(TimeSpan.FromSeconds(SolveDt)), _westStations, RampValues(_westValues));
		BoundaryConditionLine boundaryConditionLine = BoundaryConditionLine.SpatiallyVariableStage(bc, pl, payload);
		boundaryConditionLine.Name = "West";
		Polyline pl2 = new Polyline(new Point[2]
		{
			new Point(extent.MaxX, 1.0),
			new Point(extent.MaxX, 499.0)
		});
		BoundaryConditionLine boundaryConditionLine2 = BoundaryConditionLine.ConstantFlow(bc, pl2, EastFlow);
		boundaryConditionLine2.Name = "East";
		return new List<BoundaryConditionLine> { boundaryConditionLine, boundaryConditionLine2 };
	}

	public override IReadOnlyList<NamedResultPoint> GetResultPoints()
	{
		return SpatiallyVariableStageVelocityBaseParams.CreateResultPoints(100.1);
	}

	private List<Memory<double>> RampValues(List<Memory<double>> values)
	{
		List<Memory<double>> doubleSpanList = new List<Memory<double>>(values.Count);
		for (int i = 0; i < values.Count; i++)
		{
			double rampFactor = BaseParams.RampFactor((double)i * SolveDt, RampSeconds);
			double[] doubleValues = values[i].ToArray();
			for (int j = 0; j < doubleValues.Length; j++)
			{
				doubleValues[j] = InitialStage + (doubleValues[j] - InitialStage) * rampFactor;
			}
			doubleSpanList.Add(doubleValues);
		}
		return doubleSpanList;
	}

	private static void ValidateWestStageSeries(double[] stations, List<Memory<double>> values)
	{
		if (stations == null)
		{
			throw new ArgumentNullException("stations");
		}
		if (values == null)
		{
			throw new ArgumentNullException("values");
		}
		if (stations.Length == 0)
		{
			throw new ArgumentException("At least one west station is required.", "stations");
		}
		if (values.Count == 0)
		{
			throw new ArgumentException("At least one west timestep row is required.", "values");
		}
		for (int i = 0; i < values.Count; i++)
		{
			if (values[i].Length != stations.Length)
			{
				throw new ArgumentException($"West timestep row {i} has length {values[i].Length}, expected {stations.Length} (one stage per station).", "values");
			}
		}
	}
}
