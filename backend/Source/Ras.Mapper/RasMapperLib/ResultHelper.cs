using System;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Mapping;

namespace RasMapperLib;

public class ResultHelper
{
	public static RiverMap ReadGeometryInfo(string resultHdfFile)
	{
		return new RASGeometry(resultHdfFile).CreateRiverMap(loadSpatialIntersectionData: false);
	}

	public static ResultMetadata ReadResultInfo(string resultHdfFile)
	{
		RASResults rASResults = new RASResults(resultHdfFile);
		ResultMetadata resultMetadata = new ResultMetadata();
		if (Operators.CompareString(rASResults.ErrorMessage, "", TextCompare: false) != 0)
		{
			resultMetadata.ErrorMessage = rASResults.ErrorMessage;
			resultMetadata.ProfileDateTimes = new DateTime[1];
			resultMetadata.ProfileNames = new string[1];
			resultMetadata.SteadyHydraulics = rASResults.SteadyHydraulics;
			resultMetadata.QuasiUnsteadyHydraulics = rASResults.QuasiUnsteadyHydraulics;
			resultMetadata.UnsteadyHydraulics = rASResults.UnsteadyHydraulics;
			resultMetadata.UnsteadySediment = rASResults.UnsteadySediment;
			resultMetadata.UncertaintyAnalysis = rASResults.UncertaintyAnalysis;
		}
		else
		{
			try
			{
				resultMetadata.ProfileDateTimes = rASResults.ProfileDateTimes.Clone() as DateTime[];
				resultMetadata.ProfileNames = rASResults.ProfileNames.Clone() as string[];
				resultMetadata.SteadyHydraulics = rASResults.SteadyHydraulics;
				resultMetadata.QuasiUnsteadyHydraulics = rASResults.QuasiUnsteadyHydraulics;
				resultMetadata.UnsteadyHydraulics = rASResults.UnsteadyHydraulics;
				resultMetadata.UnsteadySediment = rASResults.UnsteadySediment;
				resultMetadata.UncertaintyAnalysis = rASResults.UncertaintyAnalysis;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				resultMetadata.ProfileDateTimes = new DateTime[1];
				resultMetadata.ProfileNames = new string[1];
				ProjectData.ClearProjectError();
			}
		}
		return resultMetadata;
	}

	public static ResultMetadata ParseResultInfo(RASResults res)
	{
		ResultMetadata resultMetadata = new ResultMetadata();
		if (Operators.CompareString(res.ErrorMessage, "", TextCompare: false) != 0)
		{
			resultMetadata.ErrorMessage = res.ErrorMessage;
			resultMetadata.ProfileDateTimes = new DateTime[1];
			resultMetadata.ProfileNames = new string[1];
			resultMetadata.SteadyHydraulics = res.SteadyHydraulics;
			resultMetadata.QuasiUnsteadyHydraulics = res.QuasiUnsteadyHydraulics;
			resultMetadata.UnsteadyHydraulics = res.UnsteadyHydraulics;
			resultMetadata.UnsteadySediment = res.UnsteadySediment;
			resultMetadata.UncertaintyAnalysis = res.UncertaintyAnalysis;
		}
		else
		{
			try
			{
				resultMetadata.ProfileDateTimes = res.ProfileDateTimes.Clone() as DateTime[];
				resultMetadata.ProfileNames = res.ProfileNames.Clone() as string[];
				resultMetadata.SteadyHydraulics = res.SteadyHydraulics;
				resultMetadata.QuasiUnsteadyHydraulics = res.QuasiUnsteadyHydraulics;
				resultMetadata.UnsteadyHydraulics = res.UnsteadyHydraulics;
				resultMetadata.UnsteadySediment = res.UnsteadySediment;
				resultMetadata.UncertaintyAnalysis = res.UncertaintyAnalysis;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				resultMetadata.ProfileDateTimes = new DateTime[1];
				resultMetadata.ProfileNames = new string[1];
				ProjectData.ClearProjectError();
			}
		}
		return resultMetadata;
	}
}
