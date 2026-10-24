using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.IO.ORM;
using Geospatial.PairedData;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Layers.BoundaryConditions;
using Utility.Extensions;
using Utility.Logging;
using Utility.ORM;
using Utility.Unsafe;

namespace Ras.Layers.Payloads;

public class PayloadSerializer
{
	public class PayloadEntity
	{
		[Metadata("Name")]
		public string Name { get; set; }

		[DatasetName("Description")]
		public string Description { get; set; }

		[Metadata("Variable")]
		public string Variable { get; set; }

		[Metadata("Enabled")]
		public bool IsEnabled { get; set; }

		[Metadata("Mode")]
		public DataEntryMode Mode { get; set; }

		[Metadata("External Connection")]
		public string ExternalConnection { get; set; }

		[Metadata("External Path")]
		public string ExternalPath { get; set; }

		[Metadata("Constant")]
		public double ConstantValue { get; set; }

		[Metadata("Row")]
		public int Row { get; set; }
	}

	private const string PayloadAttributes = "Payloads";

	private const string TimeSeriesDefinitionAttributes = "Time Series Definitions";

	private const string SpatiallyVariableTimeSeriesAttributes = "Spatially Variable Time Series";

	private const string RatingCurveAttributes = "Rating Curves";

	private const string ValuesDataset = "Values";

	private const string TimesDataset = "Times";

	private static readonly ILogger _logger = LogHelper.GetLogger("PayloadSerializer");

	public List<PayloadEntity> Payloads { get; set; } = new List<PayloadEntity>();

	public List<TimeSeriesPayloadEntity> TimeSeriesDefinitions { get; set; } = new List<TimeSeriesPayloadEntity>();

	public List<SpatiallyVariableTimeSeriesPayloadEntity> SpatiallyVariableTimeSeries { get; set; } = new List<SpatiallyVariableTimeSeriesPayloadEntity>();

	public List<RatingCurveDefinitionEntity> RatingCurves { get; set; } = new List<RatingCurveDefinitionEntity>();

	public Memory<double> Values { get; set; }

	public Memory<long> Times { get; set; }

	public static PayloadSerializer From(IReadOnlyList<BoundaryConditionLine> bcLines)
	{
		PayloadSerializer payloadSerializer = new PayloadSerializer();
		payloadSerializer.Payloads.Capacity = bcLines.Sum((BoundaryConditionLine bcl) => bcl.Payloads.Count);
		int ordinal = 0;
		int ordinal2 = 0;
		int ordinal3 = 0;
		List<double> valueDataset = new List<double>();
		List<long> timeDataset = new List<long>();
		foreach (BoundaryConditionLine bcLine in bcLines)
		{
			foreach (PayloadBase payload in bcLine.Payloads)
			{
				PayloadEntity payloadEntity = new PayloadEntity();
				payloadEntity.Name = payload.Name;
				payloadEntity.Description = payload.Description;
				payloadEntity.Variable = payload.Variable;
				payloadEntity.IsEnabled = payload.IsEnabled;
				if (payload is ConstantPayload constantPayload)
				{
					payloadEntity.Mode = DataEntryMode.Constant;
					payloadEntity.Row = -1;
					payloadEntity.ConstantValue = constantPayload.Value;
				}
				else if (payload is TimeSeriesPayload timeSeriesPayload)
				{
					payloadEntity.Mode = DataEntryMode.TimeSeries;
					payloadEntity.Row = ordinal++;
					TimeSeriesPayloadEntity timeSeriesPayloadEntity = TimeSeriesPayloadEntity.From(timeSeriesPayload.TimeSeriesDefinition);
					payloadSerializer.TimeSeriesDefinitions.Add(timeSeriesPayloadEntity);
					_ = valueDataset.Count;
					if (!timeSeriesPayload.Values.IsEmpty)
					{
						Memory<double> destination = GetValueSpace(timeSeriesPayload.Count, out var valueRowStart);
						timeSeriesPayloadEntity.ValueRow = valueRowStart;
						timeSeriesPayload.Values.CopyTo(destination);
						Memory<long> times = GetTimeSpace(timeSeriesPayload.Count, out var timeRowStart);
						timeSeriesPayloadEntity.TimeRow = timeRowStart;
						timeSeriesPayloadEntity.RowCount = timeSeriesPayload.Count;
						timeSeriesPayload.TimeSeriesDefinition.GetRawTimeValues(times);
					}
					else
					{
						timeSeriesPayloadEntity.ValueRow = -1;
						timeSeriesPayloadEntity.TimeRow = -1;
						timeSeriesPayloadEntity.RowCount = 0;
					}
				}
				else if (payload is RatingCurvePayload ratingCurvePayload)
				{
					payloadEntity.Mode = DataEntryMode.RatingCurve;
					payloadEntity.Row = ordinal2++;
					RatingCurveDefinitionEntity ratingCurveDefinitionEntity = new RatingCurveDefinitionEntity();
					int count = valueDataset.Count;
					Memory<double> ensureNewSpace = UnsafeHelpers.EnsureNewSpace(valueDataset, ratingCurvePayload.Count * 2);
					Memory<double> destination2 = ensureNewSpace.Slice(0, ratingCurvePayload.Count);
					Memory<double> destination3 = ensureNewSpace.Slice(ratingCurvePayload.Count);
					ratingCurvePayload.InputValues.CopyTo(destination2);
					ratingCurvePayload.OutputValues.CopyTo(destination3);
					ratingCurveDefinitionEntity.StartX = count;
					ratingCurveDefinitionEntity.StartY = count + ratingCurvePayload.Count;
					ratingCurveDefinitionEntity.RowCount = ratingCurvePayload.Count;
					ratingCurveDefinitionEntity.InputVariable = ratingCurvePayload.IndependentVariable;
					payloadSerializer.RatingCurves.Add(ratingCurveDefinitionEntity);
				}
				else if (payload is ExternalPayload externalPayload)
				{
					payloadEntity.Mode = DataEntryMode.External;
					payloadEntity.ExternalConnection = externalPayload.ExternalConnection;
					payloadEntity.ExternalPath = externalPayload.ExternalPath;
				}
				else if (payload is FunctionalPayload)
				{
					// FunctionalPayload currently has only PayloadBase metadata.
					// Persist that existing schema without inventing a formula.
					payloadEntity.Mode = DataEntryMode.Functional;
					payloadEntity.Row = -1;
				}
				else
				{
					if (!(payload is SpatiallyVariableTimeSeriesPayload spatiallyVariableTimeSeriesPayload))
					{
						throw new NotSupportedException();
					}
					payloadEntity.Mode = DataEntryMode.SpatiallyVariableTimeSeries;
					payloadEntity.Row = ordinal3++;
					SpatiallyVariableTimeSeriesPayloadEntity spatiallyVariableTimeSeriesPayloadEntity = SpatiallyVariableTimeSeriesPayloadEntity.From(spatiallyVariableTimeSeriesPayload);
					payloadSerializer.SpatiallyVariableTimeSeries.Add(spatiallyVariableTimeSeriesPayloadEntity);
					if (!spatiallyVariableTimeSeriesPayload.Stations.IsEmpty)
					{
						Memory<double> destination4 = GetValueSpace(spatiallyVariableTimeSeriesPayload.StationCount, out var valueRowStart2);
						spatiallyVariableTimeSeriesPayload.Stations.CopyTo(destination4);
						spatiallyVariableTimeSeriesPayloadEntity.StationRow = valueRowStart2;
						spatiallyVariableTimeSeriesPayloadEntity.StationCount = spatiallyVariableTimeSeriesPayload.StationCount;
					}
					else
					{
						spatiallyVariableTimeSeriesPayloadEntity.StationRow = -1;
						spatiallyVariableTimeSeriesPayloadEntity.StationCount = 0;
					}
					spatiallyVariableTimeSeriesPayload.ForceAlignValuesBuffer();
					if (!spatiallyVariableTimeSeriesPayload.Values.IsNullOrEmpty())
					{
						Memory<double> buffer = GetValueSpace(spatiallyVariableTimeSeriesPayload.TotalValueCount, out var valueRowStart3);
						spatiallyVariableTimeSeriesPayloadEntity.ValueRow = valueRowStart3;
						spatiallyVariableTimeSeriesPayloadEntity.ValueCount = buffer.Length;
						spatiallyVariableTimeSeriesPayload.CopyValuesTo(buffer);
					}
					else
					{
						spatiallyVariableTimeSeriesPayloadEntity.ValueRow = -1;
					}
					if (spatiallyVariableTimeSeriesPayload.TimeSeriesDefinition.Count == -1)
					{
						spatiallyVariableTimeSeriesPayloadEntity.TimeRow = -1;
						spatiallyVariableTimeSeriesPayloadEntity.TimeCount = 0;
					}
					else
					{
						Memory<long> times2 = GetTimeSpace(spatiallyVariableTimeSeriesPayload.TimeSeriesDefinition.Count, out var timeRowStart2);
						spatiallyVariableTimeSeriesPayloadEntity.TimeRow = timeRowStart2;
						spatiallyVariableTimeSeriesPayloadEntity.TimeCount = times2.Length;
						spatiallyVariableTimeSeriesPayload.TimeSeriesDefinition.GetRawTimeValues(times2);
					}
				}
				payloadSerializer.Payloads.Add(payloadEntity);
			}
		}
		payloadSerializer.Values = UnsafeHelpers.GetListBackingBuffer(valueDataset);
		payloadSerializer.Times = UnsafeHelpers.GetListBackingBuffer(timeDataset);
		return payloadSerializer;
		Memory<long> GetTimeSpace(int length, out int reference)
		{
			reference = timeDataset.Count;
			return UnsafeHelpers.EnsureNewSpace(timeDataset, length);
		}
		Memory<double> GetValueSpace(int length, out int reference)
		{
			reference = valueDataset.Count;
			return UnsafeHelpers.EnsureNewSpace(valueDataset, length);
		}
	}

	private TimeSeriesDefinitionBase LoadTSD(TimeSeriesDefinitionEntity tsdEntity, int row, int count)
	{
		if (tsdEntity.TimeSeriesType == TimeSeriesType.Regular)
		{
			return new RegularTimeSeriesDefinition(tsdEntity.RTSInterval, tsdEntity.FixedStartTime);
		}
		if (tsdEntity.TimeSeriesType == TimeSeriesType.Irregular)
		{
			Memory<long> longs;
			if (tsdEntity.ExpressedTimeType == ExpressedTimeType.Absolute)
			{
				longs = Times;
				longs = longs.Slice(row, count);
				Span<long> longsSpan = longs.Span;
				DateTime[] dateTimes = new DateTime[count];
				for (int i = 0; i < count; i++)
				{
					dateTimes[i] = new DateTime(longsSpan[i]);
				}
				return new IrregularAbsoluteTimeSeriesDefinition(dateTimes);
			}
			if (tsdEntity.ExpressedTimeType == ExpressedTimeType.Relative)
			{
				longs = Times;
				longs = longs.Slice(row, count);
				Span<long> longs2 = longs.Span;
				TimeSpan[] timeSpans = new TimeSpan[count];
				for (int j = 0; j < count; j++)
				{
					timeSpans[j] = TimeSpan.FromTicks(longs2[j]);
				}
				return new IrregularRelativeTimeSeriesDefinition(timeSpans);
			}
			throw new NotImplementedException();
		}
		throw new NotImplementedException();
	}

	private Memory<double> LoadValues(int start, int count)
	{
		if (count <= 0)
		{
			return Memory<double>.Empty;
		}
		return Values.Slice(start, count);
	}

	private List<Memory<double>> LoadValuesJagged(int start, int totalCount, int perRowCount)
	{
		if (totalCount <= 0)
		{
			return new List<Memory<double>>();
		}
		Memory<double> slice = Values.Slice(start, totalCount);
		int count = totalCount / perRowCount;
		List<Memory<double>> doubleSpanList = new List<Memory<double>>(count);
		for (int i = 0; i < count; i++)
		{
			doubleSpanList.Add(slice.Slice(0, perRowCount));
			slice = slice.Slice(perRowCount);
		}
		return doubleSpanList;
	}

	public void LoadInto(BoundaryConditionLine bcl, int start, int count)
	{
		if (bcl.Payloads == null)
		{
			bcl.Payloads = new List<PayloadBase>(count);
			return;
		}
		int capacity = bcl.Payloads.Count + count;
		if (bcl.Payloads.Capacity < capacity)
		{
			bcl.Payloads.Capacity = capacity;
		}
		for (int i = start; i < start + count; i++)
		{
			PayloadEntity payloadEntity = Payloads[i];
			PayloadBase payloadBase = null;
			if (payloadEntity.Mode == DataEntryMode.Constant)
			{
				payloadBase = new ConstantPayload(payloadEntity.Variable, payloadEntity.ConstantValue);
			}
			else if (payloadEntity.Mode == DataEntryMode.TimeSeries)
			{
				TimeSeriesPayloadEntity timeSeriesPayloadEntity = TimeSeriesDefinitions[payloadEntity.Row];
				Memory<double> values = LoadValues(timeSeriesPayloadEntity.ValueRow, timeSeriesPayloadEntity.RowCount);
				TimeSeriesDefinitionBase tsd = LoadTSD(timeSeriesPayloadEntity.TimeSeriesDefinition, timeSeriesPayloadEntity.TimeRow, timeSeriesPayloadEntity.RowCount);
				payloadBase = new TimeSeriesPayload(payloadEntity.Variable, tsd, values);
			}
			else if (payloadEntity.Mode == DataEntryMode.RatingCurve)
			{
				RatingCurveDefinitionEntity ratingCurveDefinitionEntity = RatingCurves[payloadEntity.Row];
				Memory<double> inputValues = LoadValues(ratingCurveDefinitionEntity.StartX, ratingCurveDefinitionEntity.RowCount);
				Memory<double> outputValues = LoadValues(ratingCurveDefinitionEntity.StartY, ratingCurveDefinitionEntity.RowCount);
				payloadBase = new RatingCurvePayload(ratingCurveDefinitionEntity.InputVariable, payloadEntity.Variable, inputValues, outputValues);
			}
			else if (payloadEntity.Mode == DataEntryMode.External)
			{
				payloadBase = new ExternalPayload(payloadEntity.Variable, payloadEntity.ExternalConnection, payloadEntity.ExternalPath);
			}
			else if (payloadEntity.Mode == DataEntryMode.Functional)
			{
				payloadBase = new FunctionalPayload(payloadEntity.Variable);
			}
			else
			{
				if (payloadEntity.Mode != DataEntryMode.SpatiallyVariableTimeSeries)
				{
					throw new NotImplementedException();
				}
				SpatiallyVariableTimeSeriesPayloadEntity spatiallyVariableTimeSeriesPayloadEntity = SpatiallyVariableTimeSeries[payloadEntity.Row];
				TimeSeriesDefinitionEntity timeSeriesDefinition = spatiallyVariableTimeSeriesPayloadEntity.TimeSeriesDefinition;
				TimeSeriesDefinitionBase tsd2 = LoadTSD(timeSeriesDefinition, spatiallyVariableTimeSeriesPayloadEntity.TimeRow, spatiallyVariableTimeSeriesPayloadEntity.TimeCount);
				payloadBase = new SpatiallyVariableTimeSeriesPayload(payloadEntity.Variable, tsd2, LoadValues(spatiallyVariableTimeSeriesPayloadEntity.StationRow, spatiallyVariableTimeSeriesPayloadEntity.StationCount), LoadValuesJagged(spatiallyVariableTimeSeriesPayloadEntity.ValueRow, spatiallyVariableTimeSeriesPayloadEntity.ValueCount, spatiallyVariableTimeSeriesPayloadEntity.StationCount));
			}
			payloadBase.Name = payloadEntity.Name;
			payloadBase.Description = payloadEntity.Description;
			payloadBase.IsEnabled = payloadEntity.IsEnabled;
			bcl.Payloads.Add(payloadBase);
		}
	}

	public void Save(H5Writer h5w, string group)
	{
		FeatureIO.Write(h5w, group, Payloads, "Payloads");
		FeatureIO.Write(h5w, group, TimeSeriesDefinitions, "Time Series Definitions");
		FeatureIO.Write(h5w, group, RatingCurves, "Rating Curves");
		FeatureIO.Write(h5w, group, SpatiallyVariableTimeSeries, "Spatially Variable Time Series");
		if (Values.Length > 0)
		{
			h5w.WriteDataset(group + "/Values", Values);
		}
		if (Times.Length > 0)
		{
			h5w.WriteDataset(group + "/Times", Times);
		}
	}

	public static PayloadSerializer Load(H5Reader h5r, string group)
	{
		PayloadSerializer payloadSerializer = new PayloadSerializer();
		payloadSerializer.Payloads = FeatureIO.Read<PayloadEntity>(h5r, group, "Payloads");
		payloadSerializer.TimeSeriesDefinitions = FeatureIO.Read<TimeSeriesPayloadEntity>(h5r, group, "Time Series Definitions");
		payloadSerializer.SpatiallyVariableTimeSeries = FeatureIO.Read<SpatiallyVariableTimeSeriesPayloadEntity>(h5r, group, "Spatially Variable Time Series");
		payloadSerializer.RatingCurves = FeatureIO.Read<RatingCurveDefinitionEntity>(h5r, group, "Rating Curves");
		string textValue = group + "/Values";
		string textValue2 = group + "/Times";
		if (h5r.ExistsAsDataset(textValue))
		{
			h5r.ReadDataset(textValue, out double[] values);
			payloadSerializer.Values = values;
		}
		if (h5r.ExistsAsDataset(textValue2))
		{
			h5r.ReadDataset(textValue2, out long[] values2);
			payloadSerializer.Times = values2;
		}
		return payloadSerializer;
	}
}
