using System;
using System.Collections.Generic;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using Utility.Systems;

namespace RasMapperLib;

public class OutputBlockCollection : IStreamableObject
{
	public const string sBaseOutput = "Base Output";

	public const string sDssHydrographOutput = "DSS Hydrograph Output";

	public const string sDssProfileOutput = "DSS Profile Output";

	private Dictionary<string, TimeProfile> _blocks;

	public Dictionary<string, TimeProfile> Blocks => _blocks;

	public int BaseProfileCount => BaseOutput?.ProfileCount ?? 0;

	public TimeProfile BaseOutput
	{
		get
		{
			if (_blocks == null)
			{
				return null;
			}
			TimeProfile timeProfile = null;
			_blocks.TryGetValue("Base Output", out timeProfile);
			return timeProfile;
		}
		set
		{
			_blocks["Base Output"] = value;
		}
	}

	public TimeProfile DSSHydrographOutput
	{
		get
		{
			if (_blocks == null)
			{
				return null;
			}
			TimeProfile timeProfile = null;
			_blocks.TryGetValue("DSS Hydrograph Output", out timeProfile);
			return timeProfile;
		}
		set
		{
			_blocks["DSS Hydrograph Output"] = value;
		}
	}

	public TimeProfile DSSProfileOutput
	{
		get
		{
			if (_blocks == null)
			{
				return null;
			}
			TimeProfile timeProfile = null;
			_blocks.TryGetValue("DSS Profile Output", out timeProfile);
			return timeProfile;
		}
		set
		{
			_blocks["DSS Profile Output"] = value;
		}
	}

	public TimeProfile SedimentProfiles { get; set; }

	public TimeProfile SedimentXSProfiles { get; set; }

	public OutputBlockCollection()
	{
		_blocks = new Dictionary<string, TimeProfile>();
	}

	public void StreamIn(BinaryReader br)
	{
		TimeProfile timeProfile = new TimeProfile();
		timeProfile.StreamIn(br);
		BaseOutput = timeProfile;
		timeProfile = new TimeProfile();
		timeProfile.StreamIn(br);
		DSSHydrographOutput = timeProfile;
		timeProfile = new TimeProfile();
		timeProfile.StreamIn(br);
		DSSProfileOutput = timeProfile;
		timeProfile = new TimeProfile();
		timeProfile.StreamIn(br);
		SedimentProfiles = timeProfile;
		timeProfile = new TimeProfile();
		timeProfile.StreamIn(br);
		SedimentXSProfiles = timeProfile;
	}

	void IStreamableObject.StreamIn(BinaryReader br)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamIn
		this.StreamIn(br);
	}

	public void StreamOut(BinaryWriter bw)
	{
		TimeProfile timeProfile = new TimeProfile();
		TimeProfile baseOutput = BaseOutput;
		if (baseOutput != null)
		{
			baseOutput.StreamOut(bw);
		}
		else
		{
			timeProfile.StreamOut(bw);
		}
		baseOutput = DSSHydrographOutput;
		if (baseOutput != null)
		{
			baseOutput.StreamOut(bw);
		}
		else
		{
			timeProfile.StreamOut(bw);
		}
		baseOutput = DSSProfileOutput;
		if (baseOutput != null)
		{
			baseOutput.StreamOut(bw);
		}
		else
		{
			timeProfile.StreamOut(bw);
		}
		baseOutput = SedimentProfiles;
		if (baseOutput != null)
		{
			baseOutput.StreamOut(bw);
		}
		else
		{
			timeProfile.StreamOut(bw);
		}
		baseOutput = SedimentXSProfiles;
		if (baseOutput != null)
		{
			baseOutput.StreamOut(bw);
		}
		else
		{
			timeProfile.StreamOut(bw);
		}
	}

	void IStreamableObject.StreamOut(BinaryWriter bw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in StreamOut
		this.StreamOut(bw);
	}

	public static OutputBlockCollection ParseUnsteady(H5Reader hr)
	{
		string outputBlockFolder = "Results/Unsteady/Output/Output Blocks";
		Dictionary<string, TimeProfile> blocks = TimeProfile.ReadAllUnsteady(hr, outputBlockFolder);
		OutputBlockCollection outputBlockCollection = new OutputBlockCollection
		{
			_blocks = blocks,
			SedimentProfiles = TimeProfile.ReadUnsteadyTimeProfile(hr, "Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series", "Sediment"),
			SedimentXSProfiles = TimeProfile.ReadUnsteadyTimeProfile(hr, "Results/Unsteady/Output/Output Blocks/Sediment SE/Sediment Time Series", "Sediment SE")
		};
		if (outputBlockCollection.BaseOutput == null)
		{
			throw new Layer.BadFileException("Malformed Unsteady Results dataset.");
		}
		return outputBlockCollection;
	}

	public static OutputBlockCollection ParseQuasiUnsteady(H5Reader hr)
	{
		OutputBlockCollection outputBlockCollection = new OutputBlockCollection
		{
			BaseOutput = TimeProfile.ReadUnsteadyTimeProfile(hr, "Results/Sediment/Output Blocks/Sediment/Sediment Time Series", "Sediment")
		};
		outputBlockCollection.SedimentProfiles = outputBlockCollection.BaseOutput;
		outputBlockCollection.SedimentXSProfiles = TimeProfile.ReadUnsteadyTimeProfile(hr, "Results/Sediment/Output Blocks/Sediment SE/Sediment Time Series", "Sediment SE");
		return outputBlockCollection;
	}

	public static OutputBlockCollection ParseSteady(H5Reader hr)
	{
		OutputBlockCollection outputBlockCollection = new OutputBlockCollection();
		outputBlockCollection._blocks = new Dictionary<string, TimeProfile>();
		outputBlockCollection.Blocks["Base Output"] = TimeProfile.ReadSteady(hr, "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles");
		return outputBlockCollection;
	}

	public static OutputBlockCollection ParseUncertaintyAnalysis(H5Reader hr)
	{
		throw new NotImplementedException();
	}

	public static OutputBlockCollection TryParseResultFile(H5Reader hr, ref string errMessage)
	{
		OutputBlockCollection result;
		try
		{
			if (hr.ExistsAsGroup("Results/Unsteady"))
			{
				result = ParseUnsteady(hr);
			}
			else if (hr.ExistsAsGroup("Results/Sediment"))
			{
				result = ParseQuasiUnsteady(hr);
			}
			else if (hr.ExistsAsGroup("Results/Steady"))
			{
				result = ParseSteady(hr);
			}
			else if (hr.ExistsAsGroup("Results/Uncertainty Analysis"))
			{
				result = ParseUncertaintyAnalysis(hr);
			}
			else
			{
				errMessage = "No known results types found.";
				result = null;
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			errMessage = ex2.StackTrace;
			result = null;
			ProjectData.ClearProjectError();
		}
		return result;
	}
}
