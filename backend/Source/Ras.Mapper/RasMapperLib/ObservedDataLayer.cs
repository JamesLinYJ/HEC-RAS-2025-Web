using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Progress;

namespace RasMapperLib;

public class ObservedDataLayer
{
	private class ObservedDataEntry
	{
		public string Type;

		public string Name;

		public string Dataset;

		public ObservedDataEntry(string type, string name, string dataset)
		{
			Type = type;
			Name = name;
			Dataset = dataset;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__33_002D0
	{
		public DateTime startDT;

		public _Closure_0024__33_002D0(_Closure_0024__33_002D0 other)
		{
			if (other != null)
			{
				startDT = other.startDT;
			}
		}

		[SpecialName]
		internal double _Lambda_0024__2(DateTime dt)
		{
			return dt.Subtract(startDT).TotalDays;
		}
	}

	public const string HDFGroupName = "/Event Conditions/Observed Data";

	public const string StageGroupName = "/Event Conditions/Observed Data/Stage";

	public const string FlowGroupName = "/Event Conditions/Observed Data/Flow";

	public const string RCGroupName = "/Event Conditions/Observed Data/Rating Curves";

	private const string sTimeSeries = "Time Series";

	public RASEventConditions EventConditions;

	private List<ObservedDataEntry> _stageEntries;

	private List<ObservedDataEntry> _flowEntries;

	private List<ObservedDataEntry> _rcEntries;

	public RASResults Results => EventConditions.Results;

	public string SourceFilename => EventConditions.SourceFilename;

	public ObservedDataLayer(RASEventConditions ec)
	{
		EventConditions = ec;
	}

	private void EnsureObservedEntriesLoaded()
	{
		if (_stageEntries == null)
		{
			LoadObservedEntries();
		}
	}

	private void LoadObservedEntries()
	{
		_stageEntries = new List<ObservedDataEntry>();
		_flowEntries = new List<ObservedDataEntry>();
		_rcEntries = new List<ObservedDataEntry>();
		if (EventConditions.SourceFileExists)
		{
			using (H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(SourceFilename))
			{
				LoadFolder(hr, "/Event Conditions/Observed Data/Stage", _stageEntries);
				LoadFolder(hr, "/Event Conditions/Observed Data/Flow", _flowEntries);
				LoadFolder(hr, "/Event Conditions/Observed Data/Rating Curves", _rcEntries);
			}
		}
	}

	private static void LoadFolder(H5Assist.VB.H5Reader hr, string folder, List<ObservedDataEntry> entries)
	{
		if (hr.ExistsAsGroup(folder))
		{
			string folderTerm = folder + "/Attributes";
			if (hr.ExistsAsDataset(folderTerm))
			{
				LoadEntries(hr.ReadCompoundDataset(folderTerm), folder, entries);
			}
		}
	}

	private static void LoadEntries(CompoundDataset ds, string folder, List<ObservedDataEntry> entries)
	{
		string[] column = ds.GetColumn<string>("Type");
		string[] column2 = ds.GetColumn<string>("Name");
		string[] column3 = ds.GetColumn<string>("Dataset");
		checked
		{
			if (column != null && column2 != null && column3 != null)
			{
				int count = column.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					entries.Add(new ObservedDataEntry(column[i], column2[i], folder + "/" + column3[i]));
				}
			}
		}
	}

	public MetTimeSeries TryReadRefICPointStage(string name)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadRefICPointData(name, _stageEntries);
	}

	private MetTimeSeries TryReadRefICPointData(string name, List<ObservedDataEntry> entries)
	{
		ObservedDataEntry observedDataEntry = entries.FirstOrDefault([SpecialName] (ObservedDataEntry e) => Operators.CompareString(e.Name, name, TextCompare: false) == 0);
		if (observedDataEntry == null)
		{
			return null;
		}
		using H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(SourceFilename);
		return MetParser.LoadProcessedTimeSeriesData(hr, observedDataEntry.Dataset, errorIfMissingMetadata: false);
	}

	public MetTimeSeries TryReadRefBCLineStage(string name)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadRefBCLineData(name, _stageEntries);
	}

	public MetTimeSeries TryReadRefBCLineFlow(string name)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadRefBCLineData(name, _flowEntries);
	}

	public MetPairedData TryReadRefBCLineRatingCurve(string name)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadRefBCLinePairedData(name, _rcEntries);
	}

	public List<MetTimeSeries> TryReadRefBCLineTS(string name)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		new List<MetTimeSeries>();
		return new List<MetTimeSeries>
		{
			TryReadRefBCLineData(name, _stageEntries),
			TryReadRefBCLineData(name, _flowEntries)
		};
	}

	private MetTimeSeries TryReadRefBCLineData(string name, List<ObservedDataEntry> entries)
	{
		ObservedDataEntry observedDataEntry = entries.FirstOrDefault([SpecialName] (ObservedDataEntry e) => Operators.CompareString(e.Name, name, TextCompare: false) == 0);
		if (observedDataEntry == null)
		{
			return null;
		}
		using H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(SourceFilename);
		return MetParser.LoadProcessedTimeSeriesData(hr, observedDataEntry.Dataset, errorIfMissingMetadata: false);
	}

	private MetPairedData TryReadRefBCLinePairedData(string name, List<ObservedDataEntry> entries)
	{
		ObservedDataEntry observedDataEntry = entries.FirstOrDefault([SpecialName] (ObservedDataEntry e) => Operators.CompareString(e.Name, name, TextCompare: false) == 0);
		if (observedDataEntry == null)
		{
			return null;
		}
		using H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(SourceFilename);
		return MetParser.LoadProcessedPairedData(hr, observedDataEntry.Dataset, errorIfMissingMetadata: false);
	}

	public MetTimeSeries TryReadCrossSectionStage(string riv, string rch, string rs)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadCrossSectionData(riv, rch, rs, _stageEntries);
	}

	public MetTimeSeries TryReadCrossSectionFlow(string riv, string rch, string rs)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadCrossSectionData(riv, rch, rs, _flowEntries);
	}

	public MetPairedData TryReadCrossSectionRatingCurve(string riv, string rch, string rs)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		return TryReadCrossSectionPairedData(riv, rch, rs, _rcEntries);
	}

	public List<MetTimeSeries> TryReadCrossSectionTS(string riv, string rch, string rs)
	{
		if (Results == null)
		{
			return null;
		}
		EnsureObservedEntriesLoaded();
		new List<MetTimeSeries>();
		return new List<MetTimeSeries>
		{
			TryReadCrossSectionData(riv, rch, rs, _stageEntries),
			TryReadCrossSectionData(riv, rch, rs, _flowEntries)
		};
	}

	private MetTimeSeries TryReadCrossSectionData(string riv, string rch, string rs, List<ObservedDataEntry> entries)
	{
		string right = riv + ", " + rch + " (" + rs + ")";
		ObservedDataEntry observedDataEntry = entries.FirstOrDefault([SpecialName] (ObservedDataEntry e) => Operators.CompareString(e.Name, right, TextCompare: false) == 0);
		if (observedDataEntry == null)
		{
			return null;
		}
		using H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(SourceFilename);
		return MetParser.LoadProcessedTimeSeriesData(hr, observedDataEntry.Dataset, errorIfMissingMetadata: false);
	}

	private MetPairedData TryReadCrossSectionPairedData(string riv, string rch, string rs, List<ObservedDataEntry> entries)
	{
		string right = riv + ", " + rch + " (" + rs + ")";
		ObservedDataEntry observedDataEntry = entries.FirstOrDefault([SpecialName] (ObservedDataEntry e) => Operators.CompareString(e.Name, right, TextCompare: false) == 0);
		if (observedDataEntry == null)
		{
			return null;
		}
		using H5Assist.VB.H5Reader hr = new H5Assist.VB.H5Reader(SourceFilename);
		return MetParser.LoadProcessedPairedData(hr, observedDataEntry.Dataset, errorIfMissingMetadata: false);
	}

	public bool PrepareForComputations(ProgressReporter prog)
	{
		_Closure_0024__33_002D0 capturedLocals = default;
		_Closure_0024__33_002D0 capturedLocals2 = new _Closure_0024__33_002D0(capturedLocals);
		if (!EventConditions.SourceFileExists)
		{
			prog.ReportWarning("Source file " + EventConditions.SourceFilename.InDoubleQuotes() + " does not exist.");
			return false;
		}
		if (EventConditions.Results == null)
		{
			prog.ReportError("Results file " + EventConditions.SourceFilename.InDoubleQuotes() + " not initialized.");
			return false;
		}
		capturedLocals2.startDT = Results.PlanAttributes.StartTime;
		DateTime endTime = Results.PlanAttributes.EndTime;
		checked
		{
			using (H5Assist.VB.H5Writer h5Writer = new H5Assist.VB.H5Writer(SourceFilename))
			{
				if (!h5Writer.ExistsAsGroup("/Event Conditions/Observed Data"))
				{
					return true;
				}
				foreach (string groupName in h5Writer.GetGroupNames("/Event Conditions/Observed Data"))
				{
					string groupNameTerm = "/Event Conditions/Observed Data/" + groupName;
					List<ObservedDataEntry> observedDataEntries = new List<ObservedDataEntry>();
					LoadFolder(h5Writer, groupNameTerm, observedDataEntries);
					string tryReadStringAttributeScalar = h5Writer.TryReadStringAttributeScalar(groupNameTerm, "Data Type");
					switch (tryReadStringAttributeScalar)
					{
					case "Time Series":
					{
						List<MetTimeSeries> tryParsePointTimeSeries = MetParser.TryParsePointTimeSeries(h5Writer, observedDataEntries.Select([SpecialName] (ObservedDataEntry entry) => entry.Dataset).ToList(), prog);
						int count = tryParsePointTimeSeries.Count - 1;
						for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
						{
							MetTimeSeries metTimeSeries = tryParsePointTimeSeries[intervalIndex];
							if (metTimeSeries == null)
							{
								continue;
							}
							ObservedDataEntry observedDataEntry2 = observedDataEntries[intervalIndex];
							List<float> values = null;
							List<DateTime> times = null;
							metTimeSeries.GetTimeSeries(capturedLocals2.startDT, endTime, ref values, ref times);
							if (times == null || times.Count == 0)
							{
								prog.ReportMessage("No observed data found for " + observedDataEntry2.Name + " within this time-window.");
								h5Writer.Delete(observedDataEntry2.Dataset);
								continue;
							}
							string[] strings = times.Select([SpecialName] (DateTime dt) => DSS.ConvertToDSSDateTime(dt)).ToArray();
							double[] col = times.Select([SpecialName] (DateTime dt) => dt.Subtract(capturedLocals2.startDT).TotalDays).ToArray();
							h5Writer.Rename(observedDataEntry2.Dataset, "tmp");
							CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
							compoundTypeDefinition.AddStringField("Date", strings.Max([SpecialName] (string dt) => dt.Length));
							compoundTypeDefinition.AddField<double>("Simulation Time");
							compoundTypeDefinition.AddField<float>("Value");
							h5Writer.Create1DCompoundDataset(observedDataEntry2.Dataset, values.Count, compoundTypeDefinition);
							using (H5Assist.VB.CompoundWriter compoundWriter2 = new H5Assist.VB.CompoundWriter(h5Writer, observedDataEntry2.Dataset, values.Count))
							{
								compoundWriter2.WriteColumn(0, strings);
								compoundWriter2.WriteColumn(1, col);
								compoundWriter2.WriteColumn(2, values.ToArray());
							}
							h5Writer.CopyAttributesTo("tmp", observedDataEntry2.Dataset);
							h5Writer.Delete("tmp");
							h5Writer.WriteAttribute(observedDataEntry2.Dataset, "Data Units", metTimeSeries.UnitString);
							h5Writer.WriteAttribute(observedDataEntry2.Dataset, "Data Type", metTimeSeries.DataType);
						}
						break;
					}
					case "Paired Data":
					{
						List<MetPairedData> tryParsePointPairedData = MetParser.TryParsePointPairedData(h5Writer, observedDataEntries.Select([SpecialName] (ObservedDataEntry entry) => entry.Dataset).ToList(), prog);
						int count2 = tryParsePointPairedData.Count - 1;
						for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
						{
							MetPairedData metPairedData = tryParsePointPairedData[intervalIndex2];
							if (metPairedData == null)
							{
								continue;
							}
							ObservedDataEntry observedDataEntry = observedDataEntries[intervalIndex2];
							if (h5Writer.GetDatasetDimensions(observedDataEntry.Dataset)[0] == 0L)
							{
								using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(h5Writer, observedDataEntry.Dataset, metPairedData.TotalCount);
								compoundWriter.WriteColumn(0, metPairedData.XValues.ToArray());
								compoundWriter.WriteColumn(1, metPairedData.YValues.ToArray());
							}
							h5Writer.WriteAttribute(observedDataEntry.Dataset, "Column Data Units", new string[2] { metPairedData.XUnitString, metPairedData.YUnitString });
						}
						break;
					}
					default:
						prog.ReportError(tryReadStringAttributeScalar.InSingleQuotes() + " data type not recognized.");
						break;
					case "Constant":
						break;
					}
				}
			}
			return true;
		}
	}
}
