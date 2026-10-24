using System;
using System.Diagnostics;
using Geospatial.PairedData;
using Utility.Hashing;
using Utility.ORM;

namespace Ras.Layers.Meteorology;

[DebuggerDisplay("{ToString(),nq}")]
public class MetGaugedData : IDataHash
{
	public static readonly DateTime NullTimestamp = DateTime.MinValue;

	private string _assignedName;

	public string AssignedName => _assignedName;

	[HashContributor]
	[Metadata("Enabled")]
	public bool IsEnabled { get; set; } = true;

	[HashContributor]
	[Metadata("Station")]
	public string StationName
	{
		get
		{
			return Station?.Name ?? _assignedName;
		}
		set
		{
			_assignedName = value;
		}
	}

	public MeteorologicStation Station { get; set; }

	[HashContributor]
	[Metadata("Type")]
	public LocalDataType LocalDataType { get; set; }

	[HashContributor]
	[Metadata("Source")]
	public DataLocation DataLocation { get; set; }

	[HashContributor]
	[Metadata("External Connection")]
	public string ConnectionName { get; set; }

	[HashContributor]
	[Metadata("External Path")]
	public string ConnectionPath { get; set; }

	[HashContributor]
	[Metadata("Constant")]
	public double Constant { get; set; }

	[HashContributor]
	[Metadata("Constant 2")]
	public double Constant2 { get; set; }

	[HashContributor]
	[DatasetName("Table", Optional = true)]
	public double[] Table { get; set; }

	[HashContributor]
	[DatasetName("Table 2", Optional = true)]
	public double[] Table2 { get; set; }

	[HashContributor]
	[ComplexMetadata]
	public TimeSeriesDefinition TableTimeSeries { get; set; } = new TimeSeriesDefinition();

	// FeatureIO flattens ComplexMetadata scalar columns but does not walk its
	// dataset properties. Keep the original time dataset names at gauge scope.
	[DatasetName("IRTS Timesteps", Optional = true)]
	public DateTime[] IrregularAbsoluteTimes
	{
		get => TableTimeSeries.IrregularAbsoluteTimes ?? Array.Empty<DateTime>();
		set => TableTimeSeries.IrregularAbsoluteTimes = value;
	}

	[DatasetName("IRTS Times (Relative)", Optional = true)]
	public double[] IrregularRelativeTimes
	{
		get => TableTimeSeries.IrregularRelativeTimes ?? Array.Empty<double>();
		set => TableTimeSeries.IrregularRelativeTimes = value;
	}

	public MetGaugedData()
	{
	}

	public MetGaugedData(MeteorologicStation station)
	{
		Station = station;
	}

	public static MetGaugedData FromTableData(MeteorologicStation station, TimeSeriesDefinition times, double[] data)
	{
		return new MetGaugedData(station)
		{
			DataLocation = DataLocation.Local,
			LocalDataType = LocalDataType.Table,
			TableTimeSeries = times,
			Table = data
		};
	}

	public TimeSeries Evaluate(DateTime startTime, DateTime endTime)
	{
		if (LocalDataType == LocalDataType.Constant)
		{
			return TimeSeries.ConstantValue(Constant);
		}
		if (LocalDataType == LocalDataType.Table)
		{
			return new TimeSeries(TableTimeSeries.Evaluate(Table.Length, startTime), Table);
		}
		_ = LocalDataType;
		_ = 2;
		throw new NotImplementedException();
	}

	public void SetConstant(double constant)
	{
		IsEnabled = true;
		LocalDataType = LocalDataType.Constant;
		Constant = constant;
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public override string ToString()
	{
		if (DataLocation == DataLocation.Remote)
		{
			return "Connection (" + ConnectionName + " -> " + ConnectionPath + ") " + (IsEnabled ? "[ENABLED]" : "[DISABLED]");
		}
		if (LocalDataType == LocalDataType.Constant)
		{
			return "Constant: " + Constant + " " + (IsEnabled ? "[ENABLED]" : "[DISABLED]");
		}
		if (LocalDataType == LocalDataType.Table)
		{
			return "Table (" + Table.Length + " Points) " + (IsEnabled ? "[ENABLED]" : "[DISABLED]");
		}
		if (LocalDataType == LocalDataType.Functional)
		{
			return "Functional (Not Supported Yet)";
		}
		return "UNKNOWN ERROR";
	}
}
