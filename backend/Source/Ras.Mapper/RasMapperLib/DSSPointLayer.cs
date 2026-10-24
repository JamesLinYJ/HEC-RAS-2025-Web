using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Hec.Dss;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class DSSPointLayer : PointFeatureLayer
{
	public const string sPath = "Path";

	public const string sRecordType = "Record Type";

	public const string sDataType = "Data Type ";

	public const string sTimeSeries = "Time Series";

	public DSSPointLayer()
	{
	}

	public DSSPointLayer(string lyrName, string srcFile)
		: base(lyrName, srcFile)
	{
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddAttributeColumn("Path", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Record Type", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Data Type ", typeof(string), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn("Time Series", typeof(TimeSeries), null, DataColumnViewState.GrayedOut);
	}

	protected override void LoadFeatureTable()
	{
		if (!SourceFileExists)
		{
			return;
		}
		try
		{
			using DssReader dssReader = new DssReader(SourceFilename);
			DssPathCollection catalog = dssReader.GetCatalog();
			foreach (DssPath catalog2 in catalog)
			{
				RecordType recordType = catalog2.RecordType;
				if (Enumerable.Contains(DSSLayer.TimeSeriesRecordTypes, catalog2.RecordType))
				{
					AddFeature(new Point(0.0, 0.0));
					int fID = checked(FeatureCount() - 1);
					SetPathName(fID, catalog2.FullPath);
					SetRecordType(fID, recordType);
					SetDataType(fID, "UNKNOWN");
					TimeSeries timeSeries = dssReader.GetTimeSeries(catalog2, DateTime.MinValue, DateTime.MinValue);
					TimeSeries timeSeries2 = new TimeSeries();
					timeSeries2.Times = timeSeries.Times;
					timeSeries2.Values = timeSeries.Values;
					SetTimeSeries(fID, timeSeries2);
				}
				else
				{
					_ = 8;
				}
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	public string GetPathName(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Path");
	}

	public void SetPathName(int fID, string name)
	{
		FeatureRow(fID)["Path"] = name;
	}

	public RecordType GetRecordType(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Record Type"]);
		if (objectValue == null || Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
		{
			return RecordType.Unknown;
		}
		return (RecordType)Conversions.ToInteger(objectValue);
	}

	public void SetRecordType(int fID, RecordType rt)
	{
		FeatureRow(fID)["Record Type"] = rt;
	}

	public string GetDataType(int fid)
	{
		return DataTableExtensions.GetString(FeatureTable(), fid, "Data Type ");
	}

	public void SetDataType(int fID, string name)
	{
		FeatureRow(fID)["Data Type "] = name;
	}

	public TimeSeries GetTimeSeries(int fid)
	{
		return FeatureRow(fid)["Time Series"] as TimeSeries;
	}

	public void SetTimeSeries(int fID, TimeSeries ts)
	{
		FeatureRow(fID)["Time Series"] = ts;
	}
}
