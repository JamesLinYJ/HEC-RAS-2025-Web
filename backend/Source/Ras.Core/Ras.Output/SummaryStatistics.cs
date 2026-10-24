using System.Collections.Generic;
using H5Assist;
using Utility.Logging;

namespace Ras.Output;

public class SummaryStatistics
{
	public static class CellDatasets
	{
		public const string DatasetName = "Cell Statistics";

		public const string MaxDepth = "Cell Max Depth";
	}

	public static class FaceDatasets
	{
		public const string DatasetName = "Face Statistics";

		public const string MaxVelocity = "Face Max Velocity";
	}

	public static class BarrelDatasets
	{
		public const string DatasetName = "Barrel Statistics";

		public const string MaxFlow = "Barrel Max Flow";
	}

	public const string H5GroupName = "Summary";

	public const string RowValueAttributeName = "Row Value";

	private readonly Dictionary<string, SummaryDataset> _datasets;

	public IReadOnlyDictionary<string, SummaryDataset> Datasets => _datasets;

	public bool HasData => _datasets.Count > 0;

	public SummaryStatistics()
	{
		_datasets = new Dictionary<string, SummaryDataset>();
	}

	public SummaryDataset GetOrCreateDataset(string datasetName, int elementCount)
	{
		if (_datasets.TryGetValue(datasetName, out var localValue))
		{
			return localValue;
		}
		SummaryDataset summaryDataset = new SummaryDataset(datasetName, elementCount);
		_datasets[datasetName] = summaryDataset;
		return summaryDataset;
	}

	public void AddRow(string datasetName, int elementCount, string rowName, double[] values)
	{
		GetOrCreateDataset(datasetName, elementCount).AddRow(rowName, values);
	}

	public bool TryGetRow(string datasetName, string rowName, out double[] values)
	{
		if (_datasets.TryGetValue(datasetName, out var localValue))
		{
			return localValue.TryGetRow(rowName, out values);
		}
		values = null;
		return false;
	}

	public OperationResult TryGetCellStatistic(string rowName, out double[] values)
	{
		return TryGetStatistic("Cell Statistics", rowName, out values);
	}

	public OperationResult TryGetFaceStatistic(string rowName, out double[] values)
	{
		return TryGetStatistic("Face Statistics", rowName, out values);
	}

	public OperationResult TryGetBarrelStatistic(string rowName, out double[] values)
	{
		return TryGetStatistic("Barrel Statistics", rowName, out values);
	}

	private OperationResult TryGetStatistic(string datasetName, string rowName, out double[] values)
	{
		values = null;
		if (!_datasets.TryGetValue(datasetName, out var localValue))
		{
			return OperationResult.Fail("Summary dataset '" + datasetName + "' not found. Result may not contain summary statistics.");
		}
		if (!localValue.TryGetRow(rowName, out values))
		{
			return OperationResult.Fail($"Statistic '{rowName}' not found in '{datasetName}'.");
		}
		return OperationResult.Success();
	}

	public void WriteTo(H5Writer hw, string outputBlockRootPath)
	{
		string parentGroup = outputBlockRootPath + "Summary/";
		foreach (SummaryDataset summaryDataset in _datasets.Values)
		{
			summaryDataset.WriteTo(hw, parentGroup);
		}
	}

	public static SummaryStatistics TryReadFrom(H5Reader hr, string outputBlockRootPath)
	{
		SummaryStatistics summaryStatistics = new SummaryStatistics();
		string pathSum = outputBlockRootPath + "Summary";
		if (!hr.ExistsAsGroup(pathSum))
		{
			return summaryStatistics;
		}
		pathSum += "/";
		foreach (string datasetName in hr.GetDatasetNames(pathSum))
		{
			string datasetPath = pathSum + datasetName;
			SummaryDataset summaryDataset = SummaryDataset.TryReadFrom(hr, datasetPath, datasetName);
			if (summaryDataset != null)
			{
				summaryStatistics._datasets[datasetName] = summaryDataset;
			}
		}
		return summaryStatistics;
	}
}
