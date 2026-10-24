using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial.IO.ORM;
using Geospatial.PairedData;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Ras.Layers.Meteorology;
using Ras.Layers.Payloads;
using Utility;
using Utility.Hashing;
using Utility.Helpers;
using Utility.Logging;
using Utility.ORM;

namespace Ras.Layers.BoundaryConditions;

public class BCLEntity_V0 : IDataHash
{
	public const string DefaultName = "Boundary Condition Line";

	private static readonly ILogger _logger = LogHelper.GetLogger("BCLEntity_V0");

	[HashContributor]
	[DatasetName("Polyline")]
	public Polyline Polyline { get; set; }

	[HashContributor]
	[Metadata("Name")]
	public string Name { get; set; }

	[HashContributor]
	[DatasetName("Description", ForceWriteIfDefault = false)]
	public string Description { get; set; }

	public BoundaryCondition BoundaryCondition { get; set; }

	[HashContributor]
	[Metadata("External Connection")]
	public string ExternalConnectionName { get; set; }

	[HashContributor]
	[Metadata("External Path")]
	public string ExternalConnectionPath { get; set; }

	[HashContributor]
	[Metadata("Local or Remote")]
	public DataLocation DataLocation { get; set; }

	[HashContributor]
	[Metadata("Local Data Type")]
	public LocalDataType LocalDataType { get; set; } = LocalDataType.Table;

	[HashContributor]
	[Metadata("Hydr Type")]
	public BoundaryConditionHydraulicType HydraulicType { get; set; } = BoundaryConditionHydraulicType.Unknown;

	[HashContributor]
	[Metadata("Constant")]
	public double ConstantValue { get; set; }

	[HashContributor]
	public double[] Stages { get; set; }

	[HashContributor]
	public double[] Flows { get; set; }

	[DatasetName("Stage-Flow Hydrograph")]
	public double[,] StageFlow { get; set; }

	public int HydroValueCount
	{
		get
		{
			double[] doubleValues;
			if (HydraulicType == BoundaryConditionHydraulicType.Stage)
			{
				doubleValues = Stages;
			}
			else
			{
				if (HydraulicType != BoundaryConditionHydraulicType.Flow)
				{
					return 0;
				}
				doubleValues = Flows;
			}
			if (doubleValues == null)
			{
				return 0;
			}
			int lengthOffset = doubleValues.Length - 1;
			while (lengthOffset >= 0 && double.IsNaN(doubleValues[lengthOffset]))
			{
				lengthOffset--;
			}
			return lengthOffset + 1;
		}
	}

	[HashContributor]
	[ComplexMetadata("Stage Hydro")]
	public StageHydroParams StageHydroParams { get; set; } = new StageHydroParams();

	[HashContributor]
	[ComplexMetadata("Flow Hydro")]
	public FlowHydroParams FlowHydroParams { get; set; } = new FlowHydroParams();

	[HashContributor]
	[ComplexMetadata]
	public TimeSeriesDefinition TableTimeSeries { get; set; } = new TimeSeriesDefinition();

	public BoundaryConditionLine ToBCLine()
	{
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine();
		boundaryConditionLine.Polyline = Polyline;
		boundaryConditionLine.Name = Name;
		boundaryConditionLine.Description = Description;
		string textValue;
		if (HydraulicType == BoundaryConditionHydraulicType.Flow)
		{
			textValue = "Flow";
		}
		else if (HydraulicType == BoundaryConditionHydraulicType.Stage)
		{
			textValue = "Stage";
		}
		else if (HydraulicType == BoundaryConditionHydraulicType.NormalDepth)
		{
			textValue = "Normal Depth";
		}
		else
		{
			_logger.LogWarning("Could not determine hydraulic variable type.");
			textValue = "Unknown";
		}
		if (DataLocation == DataLocation.Local)
		{
			if (LocalDataType == LocalDataType.Constant || HydraulicType == BoundaryConditionHydraulicType.NormalDepth)
			{
				boundaryConditionLine.Payloads.Add(new ConstantPayload(textValue, ConstantValue)
				{
					IsEnabled = true,
					Name = StringHelpers.NextUniqueName(boundaryConditionLine.Payloads, (PayloadBase pl) => pl.Name, textValue + " Payload"),
					Description = "Payload imported from v0 of the Boundary Condition file format."
				});
			}
			else if (LocalDataType == LocalDataType.Functional)
			{
				_logger.LogWarning("Cannot convert functional payload.");
			}
			else if (LocalDataType == LocalDataType.Table)
			{
				TimeSeriesDefinitionBase timeSeriesDefinitionBase = null;
				timeSeriesDefinitionBase = ((TableTimeSeries != null) ? TableTimeSeries.ToNewTimeSeries() : DefaultTimeSeries());
				Memory<double> doubleSpan = default;
				if (HydraulicType == BoundaryConditionHydraulicType.Flow)
				{
					doubleSpan = Flows;
				}
				else if (HydraulicType == BoundaryConditionHydraulicType.Stage)
				{
					doubleSpan = Stages;
				}
				else
				{
					doubleSpan = new double[HydroValueCount];
					_logger.LogWarning("Only flow/stage data allowed for tables (v0).");
				}
				boundaryConditionLine.Payloads.Add(new TimeSeriesPayload(textValue, timeSeriesDefinitionBase, doubleSpan)
				{
					IsEnabled = true,
					Name = StringHelpers.NextUniqueName(boundaryConditionLine.Payloads, (PayloadBase pl) => pl.Name, textValue + " Payload"),
					Description = "Payload imported from v0 of the Boundary Condition file format."
				});
			}
			else
			{
				_logger.LogWarning("Local data type not recognized: {Type}", LocalDataType);
			}
		}
		else
		{
			if (DataLocation != DataLocation.Remote)
			{
				throw new NotImplementedException();
			}
			_logger.LogWarning("Cannot convert external payload.");
		}
		return boundaryConditionLine;
	}

	private static TimeSeriesDefinitionBase DefaultTimeSeries()
	{
		return new RegularTimeSeriesDefinition(TimeSpan.FromHours(1));
	}

	[PostLoad]
	public void PostLoad()
	{
		if (StageFlow != null && StageFlow.GetLength(0) > 0)
		{
			int length = StageFlow.GetLength(0);
			if (StageFlow.GetLength(1) != 2)
			{
				throw new Exception();
			}
			Stages = new double[length];
			Flows = new double[length];
			for (int i = 0; i < length; i++)
			{
				Stages[i] = StageFlow[i, 0];
				Flows[i] = StageFlow[i, 1];
			}
		}
		TrimNaNsFromEndOfStagesAndFlows();
	}

	public void TrimNaNsFromEndOfStagesAndFlows()
	{
		IEnumerable<double> source = Stages.TrimNaNsFromEnd(out var trimmed);
		IEnumerable<double> source2 = Flows.TrimNaNsFromEnd(out var trimmed2);
		if (trimmed > 0)
		{
			Stages = source.ToArray();
		}
		if (trimmed2 > 0)
		{
			Flows = source2.ToArray();
		}
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}
}
