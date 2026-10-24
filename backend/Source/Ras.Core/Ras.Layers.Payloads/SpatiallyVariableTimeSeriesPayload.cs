using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers.Payloads;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class SpatiallyVariableTimeSeriesPayload : PayloadBase, ITimeSeriesPayload
{
	private static readonly ILogger _logger = LogHelper.GetLogger("SpatiallyVariableTimeSeriesPayload");

	[HashContributor]
	public TimeSeriesDefinitionBase TimeSeriesDefinition { get; set; }

	[HashContributor]
	public Memory<double> Stations { get; set; }

	[HashContributor]
	public List<Memory<double>> Values { get; set; }

	public int StationCount => Stations.Length;

	public int TimeCount => TimeSeriesDefinition.Count;

	public int TotalValueCount => Values?.Sum((Memory<double> vmem) => vmem.Length) ?? 0;

	public SpatiallyVariableTimeSeriesPayload(string variable, TimeSeriesDefinitionBase tsd, Memory<double> stations, List<Memory<double>> values)
		: base(variable)
	{
		TimeSeriesDefinition = tsd;
		Stations = stations;
		Values = values;
	}

	public void SafeSetStations(Memory<double> newStations)
	{
		int stationCount = StationCount;
		_ = TimeCount;
		Stations = newStations.ToArray();
		if (Values == null)
		{
			_logger.LogWarning("Values was null in SafeSetStations");
			Values = new List<Memory<double>>();
			return;
		}
		Memory<double> doubleSpan = new double[Values.Count * newStations.Length];
		for (int i = 0; i < Values.Count; i++)
		{
			Memory<double> row = Values[i];
			if (row.Length != stationCount)
			{
				_logger.LogWarning("SafeSetStations: Value Row [{RowIndex}] doesn't match expected stationing count {StationCount}.", i, stationCount);
			}
			Memory<double> slice = doubleSpan.Slice(0, newStations.Length);
			doubleSpan = doubleSpan.Slice(newStations.Length);
			CopyRowWithPadding(row, slice);
			Values[i] = slice;
		}
	}

	private static void CopyRowWithPadding(Memory<double> row, Memory<double> newRow)
	{
		if (row.Length > newRow.Length)
		{
			row.Slice(0, newRow.Length).CopyTo(newRow);
			return;
		}
		newRow.Span.Fill(double.NaN);
		row.CopyTo(newRow);
	}

	public void ForceAlignValuesBuffer()
	{
		if (Values == null)
		{
			Values = new List<Memory<double>>();
			return;
		}
		int stationCount = StationCount;
		for (int i = 0; i < Values.Count; i++)
		{
			Memory<double> row = Values[i];
			if (row.Length != stationCount)
			{
				Memory<double> doubleSpan = new double[stationCount];
				CopyRowWithPadding(row, doubleSpan);
				Values[i] = doubleSpan;
			}
		}
	}

	public void CopyValuesTo(Memory<double> buffer)
	{
		for (int i = 0; i < Values.Count; i++)
		{
			Memory<double> doubleSpan = Values[i];
			doubleSpan.CopyTo(buffer);
			buffer = buffer.Slice(doubleSpan.Length);
		}
	}

	public OperationResult CoversTimeWindow(DateTime startTime, TimeSpan duration, DateTime bcStartTime)
	{
		OperationResult operationResult = ValidateForCompute();
		if (!operationResult)
		{
			return operationResult;
		}
		if (!TimeSeriesDefinition.CoversTimeWindow(startTime, duration, Values.Count, bcStartTime))
		{
			operationResult.AddFailure("Time-Series definition does not cover the desired time-window.");
		}
		return operationResult;
	}

	internal OperationResult TryEvaluateAtFaces(IReadOnlyList<ProfileFaceRange> faceRanges, DateTime boundaryConditionStartTime, out int[] faces, out double[] timesteps, out double[,] faceValues)
	{
		faces = Array.Empty<int>();
		timesteps = Array.Empty<double>();
		faceValues = new double[0, 0];
		OperationResult operationResult = ValidateForCompute();
		if (!operationResult)
		{
			return operationResult;
		}
		if (faceRanges == null || faceRanges.Count == 0)
		{
			return OperationResult.Fail("Spatially variable boundary condition did not intersect any mesh faces.");
		}
		faces = new int[faceRanges.Count];
		timesteps = TimeSeriesDefinition.GetRelativeTimeSeconds(Values.Count, boundaryConditionStartTime);
		faceValues = new double[Values.Count, faceRanges.Count];
		Memory<double> stationsSpan = Stations;
		ReadOnlySpan<double> stations = stationsSpan.Span;
		for (int i = 0; i < faceRanges.Count; i++)
		{
			ProfileFaceRange profileFaceRange = faceRanges[i];
			faces[i] = profileFaceRange.Face;
			double station = 0.5 * (profileFaceRange.StationStart + profileFaceRange.StationEnd);
			for (int j = 0; j < Values.Count; j++)
			{
				double[,] faceValues2 = faceValues;
				int ordinal = j;
				int ordinal2 = i;
				stationsSpan = Values[j];
				faceValues2[ordinal, ordinal2] = Interpolate(stations, stationsSpan.Span, station);
			}
		}
		return true;
	}

	internal OperationResult ValidateForCompute()
	{
		if (TimeSeriesDefinition == null)
		{
			return OperationResult.Fail("Spatially variable time series has no time definition.");
		}
		Memory<double> stations = Stations;
		if (stations.IsEmpty)
		{
			return OperationResult.Fail("Spatially variable time series has no stations.");
		}
		if (Values == null || Values.Count == 0)
		{
			return OperationResult.Fail("Spatially variable time series has no value rows.");
		}
		if (TimeSeriesDefinition.Count >= 0 && TimeSeriesDefinition.Count != Values.Count)
		{
			return OperationResult.Fail("Spatially variable time-series row count does not match its time definition.");
		}
		stations = Stations;
		ReadOnlySpan<double> stationsSpan = stations.Span;
		double negativeInfinity = double.NegativeInfinity;
		for (int i = 0; i < stationsSpan.Length; i++)
		{
			double station = stationsSpan[i];
			if (!double.IsFinite(station) || station <= negativeInfinity)
			{
				return OperationResult.Fail("Spatially variable time-series stations must be finite and strictly increasing.");
			}
			negativeInfinity = station;
		}
		for (int j = 0; j < Values.Count; j++)
		{
			stations = Values[j];
			ReadOnlySpan<double> stations2 = stations.Span;
			if (stations2.Length != stationsSpan.Length)
			{
				return OperationResult.Fail("Spatially variable time-series rows must match the station count.");
			}
			for (int k = 0; k < stations2.Length; k++)
			{
				if (!double.IsFinite(stations2[k]))
				{
					return OperationResult.Fail("Spatially variable time-series values must be finite.");
				}
			}
		}
		return true;
	}

	private static double Interpolate(ReadOnlySpan<double> stations, ReadOnlySpan<double> values, double station)
	{
		if (station <= stations[0])
		{
			return values[0];
		}
		if (station >= stations[stations.Length - 1])
		{
			return values[values.Length - 1];
		}
		int intervalIndex = 0;
		int lengthOffset = stations.Length - 1;
		while (lengthOffset - intervalIndex > 1)
		{
			int ordinal = (intervalIndex + lengthOffset) / 2;
			if (stations[ordinal] <= station)
			{
				intervalIndex = ordinal;
			}
			else
			{
				lengthOffset = ordinal;
			}
		}
		double interpolationFraction = (station - stations[intervalIndex]) / (stations[lengthOffset] - stations[intervalIndex]);
		return values[intervalIndex] + interpolationFraction * (values[lengthOffset] - values[intervalIndex]);
	}

	public OperationResult DataEquals(SpatiallyVariableTimeSeriesPayload other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("SpatiallyVariableTimeSeriesPayload hash mismatch.");
		}
		OperationResult operationResult2 = TimeSeriesDefinition.DataEquals(other.TimeSeriesDefinition);
		if (!operationResult2)
		{
			operationResult.AddFailure(operationResult2);
		}
		Memory<double> stations = Stations;
		ReadOnlySpan<double> a = stations.Span;
		stations = other.Stations;
		if (!MathHelpers.SpanDoubleEquals(a, stations.Span))
		{
			operationResult.AddFailure("Stations do not match.");
		}
		if (Values.Count != other.Values.Count)
		{
			operationResult.AddFailure("Values List<> size mismatch.");
		}
		else
		{
			for (int i = 0; i < Values.Count; i++)
			{
				Memory<double> doubleSpan = Values[i];
				Memory<double> doubleSpan2 = other.Values[i];
				if (!MathHelpers.SpanDoubleEquals(doubleSpan.Span, doubleSpan2.Span))
				{
					operationResult.AddFailure("Values in row " + i + " do not match.");
				}
			}
		}
		return operationResult;
	}

	private string GetDebuggerDisplay()
	{
		return (Name ?? "(No Name)") + " " + Variable + ": " + StationCount + " x " + TimeCount;
	}
}
