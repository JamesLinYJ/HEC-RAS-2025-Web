using System;
using System.Collections.Generic;
using System.Linq;
using Geospatial;
using H5Assist;
using H5Assist.Caching;
using Ras.Layers;
using Utility.Progress;

namespace Ras.Output;

public class LegacyOutputBlock : IHorizontalOutputBlock, IOutputBlock, IDiscreteTimestepsWithEvent, IDiscreteTimesteps
{
	public const string H5OutputBlockRootDirectory = "/Results/Unsteady/Output/Output Blocks/";

	public const string MaxProfileName = "Max";

	public const string MinProfileName = "Min";

	public const string NullProfileName = "(None)";

	public const int MaxProfileIndex = int.MaxValue;

	public const int MinProfileIndex = int.MinValue;

	public const int NullProfileIndex = -1;

	private H5RowCache<float> _rowCache;

	public List<DateTime> Timesteps { get; set; }

	public IReadOnlyList<DateTime> Times => Timesteps;

	public IList<string> ProfileNames { get; set; }

	public int ProfileCount => ProfileNames.Count;

	public OutputBlockSummary Summary { get; }

	public Result Result { get; }

	public Geometry Geometry => Result.Geometry;

	public string Name { get; }

	public string RootPath { get; private set; }

	public string UnsteadyPath { get; private set; }

	public string UnsteadyFlowAreaPath { get; private set; }

	public string SummaryPath { get; private set; }

	public string SummaryFlowAreaPath { get; private set; }

	public string UnsteadyStorageAreaPath { get; private set; }

	public string UnsteadyStorageAreaWSELPath { get; private set; }

	public string SummaryStorageAreaPath { get; private set; }

	public string SummaryStorageAreaMaxWSELPath { get; private set; }

	public string UnsteadyCrossSectionPath { get; private set; }

	public string SummaryCrossSectionPath { get; private set; }

	public bool Success { get; }

	public List<OutputVariable> OutputVariables { get; }

	public event TimestepAddedEvent TimestepAdded
	{
		add
		{
		}
		remove
		{
		}
	}

	public static bool TryLoadOutputBlocks(Result res, out List<LegacyOutputBlock> blocks, ProgressReporter err = null)
	{
		try
		{
			if (err == null)
			{
				err = ProgressReporter.None();
			}
			blocks = res.H5Wrapper.UseResource((H5Reader hr) =>
			{
				TryLoadOutputBlocks(res, hr, out var blocks2, err);
				return blocks2;
			});
			return blocks != null;
		}
		catch
		{
			blocks = null;
			return false;
		}
	}

	public static bool TryLoadOutputBlocks(Result res, H5Reader hr, out List<LegacyOutputBlock> blocks, ProgressReporter err = null)
	{
		try
		{
			if (err == null)
			{
				err = ProgressReporter.None();
			}
			if (!hr.ExistsAsGroup("/Results/Unsteady/Output/Output Blocks/"))
			{
				blocks = null;
				return false;
			}
			blocks = new List<LegacyOutputBlock>();
			foreach (string groupName in hr.GetGroupNames("/Results/Unsteady/Output/Output Blocks/"))
			{
				if (TryLoadOutputBlock(hr, groupName, res, out var block, err))
				{
					blocks.Add(block);
				}
			}
			return true;
		}
		catch
		{
			blocks = null;
			return false;
		}
	}

	private static bool TryLoadOutputBlock(H5Reader h5r, string name, Result res, out LegacyOutputBlock block, ProgressReporter err)
	{
		string absolutePath = "/Results/Unsteady/Output/Output Blocks/" + name;
		if (!h5r.ExistsAsAttribute(absolutePath, "Output Block Name"))
		{
			block = null;
			return false;
		}
		block = new LegacyOutputBlock(res, name);
		h5r.ReadDataset(block.RootPath + "/Unsteady Time Series/Time", out double[] values);
		block.Timesteps = new List<DateTime>();
		DateTime startTime = res.Plan.StartTime;
		block.Timesteps.AddRange(values.Select((double d) => startTime.AddDays(d)));
		h5r.ReadDataset(block.RootPath + "/Unsteady Time Series/Time Date Stamp", out var values2);
		block.ProfileNames = values2.ToList();
		return true;
	}

	private LegacyOutputBlock(Result res, string name)
	{
		Result = res;
		Name = name;
		_rowCache = new H5RowCache<float>(res.Filename);
		InitPaths();
	}

	private void InitPaths()
	{
		RootPath = "/Results/Unsteady/Output/Output Blocks/" + Name;
		UnsteadyPath = RootPath + "/Unsteady Time Series";
		UnsteadyFlowAreaPath = UnsteadyPath + "/2D Flow Areas";
		SummaryPath = RootPath + "/Summary Output";
		SummaryFlowAreaPath = SummaryPath + "/2D Flow Areas";
		UnsteadyStorageAreaPath = UnsteadyPath + "/Storage Areas";
		UnsteadyStorageAreaWSELPath = UnsteadyStorageAreaPath + "/Water Surface";
		SummaryStorageAreaPath = SummaryPath + "/Storage Areas";
		SummaryStorageAreaMaxWSELPath = SummaryStorageAreaPath + "/Maximum Water Surface";
		UnsteadyCrossSectionPath = UnsteadyPath + "/Cross Sections";
		SummaryCrossSectionPath = SummaryPath + "/Cross Sections";
	}

	public string ExpectedDatasetName(OutputVariable variable)
	{
		string name = Result.Geometry.FlowAreaLayer[0].Name;
		return UnsteadyFlowAreaPath + "/" + name + "/" + variable.Name;
	}

	public string Get2DOutputDatasetName(OutputVariable variable, string meshName, bool isSummary)
	{
		if (isSummary)
		{
			return SummaryFlowAreaPath + "/" + meshName + "/Maximum " + variable.Name;
		}
		return UnsteadyFlowAreaPath + "/" + meshName + "/" + variable.Name;
	}

	public static string Get2DOutputDatasetName(OutputVariable variable, string blockName, string meshName, bool isSummary)
	{
		if (isSummary)
		{
			return string.Concat("/Results/Unsteady/Output/Output Blocks/" + blockName + "/Summary Output/2D Flow Areas/" + meshName + "/", "Maximum ", variable.Name);
		}
		return string.Concat("/Results/Unsteady/Output/Output Blocks/" + blockName + "/Unsteady Time Series/2D Flow Areas/" + meshName + "/", variable.Name);
	}

	public bool TryReadPerMesh(OutputVariable variable, int profile, Memory<float>[] values)
	{
		FlowAreaLayer flowAreaLayer = Result.Geometry.FlowAreaLayer;
		for (int i = 0; i < flowAreaLayer.Count; i++)
		{
			if (!values[i].IsEmpty)
			{
				string name = flowAreaLayer[i].Name;
				if (profile == int.MaxValue)
				{
					string dataset = SummaryFlowAreaPath + "/" + name + "/Maximum " + variable.Name;
					_rowCache.GetRow(dataset, 0, values[i]);
				}
				else
				{
					string dataset2 = UnsteadyFlowAreaPath + "/" + name + "/" + variable.Name;
					_rowCache.GetRow(dataset2, profile, values[i]);
				}
			}
		}
		return true;
	}

	public bool TryReadPerStorageArea(OutputVariable variable, int profile, out float[,] values)
	{
		if (variable != Ras.Output.OutputVariables.Legacy_WaterSurface)
		{
			throw new ArgumentException("Must be WaterSurface currently");
		}
		string dset;
		if (profile == int.MaxValue)
		{
			dset = SummaryStorageAreaMaxWSELPath;
		}
		else
		{
			dset = UnsteadyStorageAreaWSELPath;
		}
		float[,] useReader = _rowCache.UseReader((H5Reader hr) =>
		{
			if (hr.ExistsAsDataset(dset))
			{
				hr.ReadDataset(dset, out float[,] values2);
				return values2;
			}
			return (float[,])null;
		});
		values = useReader;
		return values != null;
	}

	public int ProfileIndex(string name)
	{
		if (name == "Max")
		{
			return int.MaxValue;
		}
		if (name == "Min")
		{
			return int.MinValue;
		}
		if (ProfileNames != null)
		{
			for (int i = 0; i < ProfileCount; i++)
			{
				if (name == ProfileNames[i])
				{
					return i;
				}
			}
		}
		return -1;
	}

	public string ProfileName(int index)
	{
		if (index == int.MaxValue)
		{
			return "Max";
		}
		if (index == int.MinValue)
		{
			return "Min";
		}
		if (index >= 0 && index < ProfileCount)
		{
			return ProfileNames[index];
		}
		return "(None)";
	}

	public int ClosestProfileIndex(string profileName, out int actualProfileName)
	{
		throw new NotImplementedException();
	}

	public int BestProfileIndex(int indexFromXML, string nameFromXML)
	{
		if (!Success)
		{
			return -1;
		}
		if (ProfileCount == 0)
		{
			return -1;
		}
		throw new NotImplementedException();
	}
}
