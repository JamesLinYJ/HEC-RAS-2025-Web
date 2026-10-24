using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Ras.Layers;

public class HydraulicDesignData
{
	private static readonly ILogger _logger = LogHelper.GetLogger("HydraulicDesignData");

	private List<StableChannelDesign> _stableChannelDesigns;

	private Project _project;

	public IReadOnlyList<StableChannelDesign> StableChannelDesigns => _stableChannelDesigns;

	public HydraulicDesignData(Project project)
	{
		_project = project;
	}

	public void InitEmpty()
	{
		_stableChannelDesigns = new List<StableChannelDesign>();
	}

	public void LoadAllHydraulicDesignData()
	{
		LoadAllBridgeScourDataInProject();
		LoadAllRipRapDataInProject();
		LoadAllUniformFlowDataInProject();
		LoadAllSedimentTransportCapacityInProject();
		LoadAllSIAMDataInProject();
		LoadAllRatingCurveDataInProject();
		LoadAllStableChannelDesignDataInProject();
	}

	public void AddStableChannelDesign(StableChannelDesign scd)
	{
		_stableChannelDesigns.Add(scd);
	}

	private void LoadAllStableChannelDesignDataInProject()
	{
		_stableChannelDesigns = _project.Loader(_project.HydraulicDesignDirectory, (string fn) => new StableChannelDesign(fn)
		{
			Project = _project
		}, "*.scd.h5");
	}

	private void LoadAllBridgeScourDataInProject()
	{
	}

	private void LoadAllRipRapDataInProject()
	{
	}

	private void LoadAllUniformFlowDataInProject()
	{
	}

	private void LoadAllSedimentTransportCapacityInProject()
	{
	}

	private void LoadAllSIAMDataInProject()
	{
	}

	private void LoadAllRatingCurveDataInProject()
	{
	}
}
