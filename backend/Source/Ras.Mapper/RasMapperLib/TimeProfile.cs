using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;
using Utility.Systems;

namespace RasMapperLib;

public class TimeProfile : IStreamableObject
{
	public string BlockName;

	public double[] SimulationTimes;

	public double[] ProfileDates;

	public string[] ProfileNames;

	public float[] ComputeTimeSteps;

	public int ProfileCount
	{
		get
		{
			if (ProfileNames == null)
			{
				return 0;
			}
			return ProfileNames.Length;
		}
	}

	public DateTime[] GetDateTimes(DateTime simStart)
	{
		int profileNameLength = 0;
		if (ProfileNames != null)
		{
			profileNameLength = ProfileNames.Length;
		}
		checked
		{
			DateTime[] dateTimes = new DateTime[profileNameLength - 1 + 1];
			if (SimulationTimes.IsNullOrEmpty())
			{
				return dateTimes;
			}
			int count = profileNameLength - 1;
			for (int i = 0; i <= count; i++)
			{
				dateTimes[i] = simStart + TimeSpan.FromDays(SimulationTimes[i]);
			}
			return dateTimes;
		}
	}

	public int ClosestProfileIndex(string profileName)
	{
		if (ProfileNames.IsNullOrEmpty())
		{
			return -1;
		}
		if (Operators.CompareString(profileName.ToLowerInvariant(), "Max".ToLowerInvariant(), TextCompare: false) == 0 || Operators.CompareString(profileName.ToLowerInvariant(), "Max WS".ToLowerInvariant(), TextCompare: false) == 0)
		{
			return int.MaxValue;
		}
		if (Operators.CompareString(profileName.ToLowerInvariant(), "Min".ToLowerInvariant(), TextCompare: false) == 0 || Operators.CompareString(profileName.ToLowerInvariant(), "Min WS".ToLowerInvariant(), TextCompare: false) == 0)
		{
			return int.MinValue;
		}
		if (ProfileDates != null)
		{
			return DSS.NearestDSSDateIndex(ProfileNames, profileName);
		}
		int indexOf = ArrayExtensionMethods.IndexOf(ProfileNames, profileName);
		if (indexOf == -1)
		{
			return 0;
		}
		return indexOf;
	}

	public string GetXSDatasetPath(RASResults.HydraulicsComputeMethod hydrType, string varname)
	{
		return hydrType switch
		{
			RASResults.HydraulicsComputeMethod.Steady => "Results/Steady/Output/Output Blocks/" + BlockName + "/Unsteady Time Series/Cross Sections/" + varname, 
			RASResults.HydraulicsComputeMethod.Quasi => "Results/Sediment/Output Blocks" + BlockName + "/Unsteady Time Series/Cross Sections/" + varname, 
			RASResults.HydraulicsComputeMethod.Unsteady => "Results/Unsteady/Output/Output Blocks/" + BlockName + "/Unsteady Time Series/Cross Sections/" + varname, 
			_ => "", 
		};
	}

	public string GetSADatasetPath(RASResults.HydraulicsComputeMethod hydrType, string varname)
	{
		return hydrType switch
		{
			RASResults.HydraulicsComputeMethod.Steady => "Results/Steady/Output/Output Blocks/" + BlockName + "/Unsteady Time Series/Storage Areas/" + varname, 
			RASResults.HydraulicsComputeMethod.Quasi => "Results/Sediment/Output Blocks" + BlockName + "/Unsteady Time Series/Storage Areas/" + varname, 
			RASResults.HydraulicsComputeMethod.Unsteady => "Results/Unsteady/Output/Output Blocks/" + BlockName + "/Unsteady Time Series/Storage Areas/" + varname, 
			_ => "", 
		};
	}

	public string Get2DDatasetPath(RASResults.HydraulicsComputeMethod hydrType, string meshName, string varname)
	{
		return hydrType switch
		{
			RASResults.HydraulicsComputeMethod.Steady => throw new NotSupportedException(), 
			RASResults.HydraulicsComputeMethod.Quasi => throw new NotSupportedException(), 
			RASResults.HydraulicsComputeMethod.Unsteady => "Results/Unsteady/Output/Output Blocks/" + BlockName + "/Unsteady Time Series/2D Flow Areas/" + meshName + "/" + varname, 
			_ => "", 
		};
	}

	public string GetSA2DDatasetPath(RASResults.HydraulicsComputeMethod hydrType, string sa2DConnectionName, string varname)
	{
		return hydrType switch
		{
			RASResults.HydraulicsComputeMethod.Steady => throw new NotSupportedException(), 
			RASResults.HydraulicsComputeMethod.Quasi => throw new NotSupportedException(), 
			RASResults.HydraulicsComputeMethod.Unsteady => "Results/Unsteady/Output/Output Blocks/" + BlockName + "/Unsteady Time Series/SA 2D Area Conn/" + sa2DConnectionName + "/" + varname, 
			_ => "", 
		};
	}

	public static TimeProfile ReadUnsteadyTimeProfile(H5Reader hr, string timeFolder, string blockName)
	{
		if (!hr.ExistsAsGroup(timeFolder))
		{
			return null;
		}
		TimeProfile timeProfile = new TimeProfile();
		timeProfile.BlockName = blockName;
		string timeFolderTerm = timeFolder + "/Time Date Stamp";
		string timeFolderTerm2 = timeFolder + "/Time Date Stamp (ms)";
		string timeFolderTerm3 = timeFolder + "/Time Step";
		timeProfile.ProfileNames = new string[1];
		checked
		{
			if (hr.ExistsAsDataset(timeFolderTerm2))
			{
				H5ReadStringColumn(hr, timeFolderTerm2, ref timeProfile.ProfileNames);
				bool condition = false;
				int count = timeProfile.ProfileNames.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					string profileName = timeProfile.ProfileNames[i];
					if (!string.IsNullOrEmpty(profileName) && profileName.Length >= 4 && Operators.CompareString(profileName.Substring(profileName.Length - 4, 4), ":000", TextCompare: false) != 0)
					{
						condition = true;
					}
				}
				if (!condition && hr.ExistsAsDataset(timeFolderTerm))
				{
					H5ReadStringColumn(hr, timeFolderTerm, ref timeProfile.ProfileNames);
				}
			}
			else if (hr.ExistsAsDataset(timeFolderTerm))
			{
				H5ReadStringColumn(hr, timeFolderTerm, ref timeProfile.ProfileNames);
			}
			if (hr.ExistsAsDataset(timeFolderTerm3))
			{
				hr.ReadDataset(timeFolderTerm3, ref timeProfile.ComputeTimeSteps);
			}
			if (timeProfile.ProfileNames.Count() == 1 && timeProfile.ProfileNames[0] == null)
			{
				timeProfile.ProfileNames = new string[0];
			}
			string timeFolderTerm4 = timeFolder + "/Time";
			timeProfile.SimulationTimes = new double[1];
			if (hr.ExistsAsDataset(timeFolderTerm4) && !timeProfile.ProfileNames.IsNullOrEmpty())
			{
				hr.ReadDataset(timeFolderTerm4, ref timeProfile.SimulationTimes);
				double toOADate = DSS.ConvertFromDSSDateTime(timeProfile.ProfileNames.First()).ToOADate();
				timeProfile.ProfileDates = new double[timeProfile.ProfileNames.Count() - 1 + 1];
				int count2 = timeProfile.ProfileNames.Count() - 1;
				for (int j = 0; j <= count2; j++)
				{
					timeProfile.ProfileDates[j] = toOADate + timeProfile.SimulationTimes[j];
				}
			}
			return timeProfile;
		}
	}

	public static Dictionary<string, TimeProfile> ReadAllUnsteady(H5Reader hr, string outputBlockFolder)
	{
		if (!hr.ExistsAsGroup(outputBlockFolder))
		{
			return null;
		}
		Dictionary<string, TimeProfile> timeProfileByString = new Dictionary<string, TimeProfile>();
		List<string> groupNames = hr.GetGroupNames(outputBlockFolder);
		foreach (string groupName in groupNames)
		{
			string concat = string.Concat(outputBlockFolder + "/" + groupName, "/Unsteady Time Series");
			if (hr.ExistsAsGroup(concat))
			{
				TimeProfile timeProfile = ReadUnsteadyTimeProfile(hr, concat, groupName);
				if (timeProfile != null)
				{
					timeProfileByString[groupName] = timeProfile;
				}
			}
		}
		return timeProfileByString;
	}

	public static TimeProfile ReadSteady(H5Reader hr, string baseFolder)
	{
		TimeProfile timeProfile = new TimeProfile();
		string baseFolderTerm = baseFolder + "/Profile Names";
		if (hr.ExistsAsDataset(baseFolderTerm))
		{
			H5ReadStringColumn(hr, baseFolderTerm, ref timeProfile.ProfileNames);
			return timeProfile;
		}
		return null;
	}

	public static TimeProfile ReadUncertainty(H5Reader hr, string uncertaintyDataset)
	{
		TimeProfile timeProfile = new TimeProfile();
		if (!hr.ExistsAsDataset(uncertaintyDataset))
		{
			return null;
		}
		long[] datasetDimensions = hr.GetDatasetDimensions(uncertaintyDataset);
		checked
		{
			timeProfile.ProfileNames = new string[(int)datasetDimensions[0] - 1 + 1];
			int count = timeProfile.ProfileNames.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				timeProfile.ProfileNames[i] = "Run" + Strings.Format(i);
			}
			return timeProfile;
		}
	}

	private static void H5ReadStringColumn(H5Reader hr, string ds, ref string[] col)
	{
		checked
		{
			switch (hr.GetDatasetNDims(ds))
			{
			case 1:
			{
				col = new string[1];
				hr.ReadDataset(ds, ref col);
				int count = col.Count() - 1;
				for (int j = 0; j <= count; j++)
				{
					col[j] = col[j].Trim();
				}
				break;
			}
			case 2:
			{
				string[,] values = new string[1, 1];
				hr.ReadDataset(ds, ref values);
				col = new string[values.GetUpperBound(1) + 1];
				int upperBound = values.GetUpperBound(1);
				for (int i = 0; i <= upperBound; i++)
				{
					col[i] = values[0, i].Trim();
				}
				break;
			}
			}
		}
	}

	public void StreamOut(BinaryWriter bw)
	{
		Pipe.WriteArray(bw, SimulationTimes);
		Pipe.WriteArray(bw, ProfileDates);
		bw.WriteArrayString(ProfileNames);
	}

	void IStreamableObject.StreamOut(BinaryWriter bw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamOut
		this.StreamOut(bw);
	}

	public void StreamIn(BinaryReader br)
	{
		SimulationTimes = Pipe.ReadArray<double>(br);
		ProfileDates = Pipe.ReadArray<double>(br);
		ProfileNames = br.ReadArrayString();
	}

	void IStreamableObject.StreamIn(BinaryReader br)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamIn
		this.StreamIn(br);
	}
}
