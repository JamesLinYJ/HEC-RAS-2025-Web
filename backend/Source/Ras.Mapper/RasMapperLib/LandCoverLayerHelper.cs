using System.Collections.Generic;
using System.Linq;
using Geospatial.Rasters.Classifications;
using Utility.Extensions;

namespace RasMapperLib;

public class LandCoverLayerHelper
{
	public const string NLCDStandardKey = "NLCD 2016";

	public const string Anderson2StandardKey = "Anderson II";

	public const string NOAACCAPStandardKey = "NOAA C-CAP";

	public string Title;

	public string DefaultFilenamePrefix;

	public List<string> ExtraColumnNames;

	public List<string> ExtraColumnFieldHeader;

	public List<string> ExtraColumnDatasets;

	public bool UseFirstExtraColumnWhenRaster;

	public Dictionary<string, List<LandCoverCategory>> WellKnownSchemes;

	public string HDFFilter => Title + " (*.hdf)|*.hdf";

	public LandCoverLayerHelper()
	{
		Title = "Land Classification";
		DefaultFilenamePrefix = "LandClassification";
		ExtraColumnNames = new List<string>();
		ExtraColumnFieldHeader = new List<string>();
		ExtraColumnDatasets = new List<string>();
		UseFirstExtraColumnWhenRaster = false;
		WellKnownSchemes = new Dictionary<string, List<LandCoverCategory>>();
	}

	public void AddColumn(string dataColHeader, string shpFieldHeader, string dsetName)
	{
		ExtraColumnNames.Add(dataColHeader);
		ExtraColumnFieldHeader.Add(shpFieldHeader);
		ExtraColumnDatasets.Add(dsetName);
	}

	public static LandCoverLayerHelper ManningsN()
	{
		LandCoverLayerHelper landCoverLayerHelper = new LandCoverLayerHelper();
		landCoverLayerHelper.Title = "Land Cover Layer";
		landCoverLayerHelper.DefaultFilenamePrefix = "LandCover";
		landCoverLayerHelper.AddColumn("Manning's n", "Manning's n", "ManningsN");
		landCoverLayerHelper.AddColumn("Percent Impervious", "Percent Impervious", "Percent Impervious");
		landCoverLayerHelper.WellKnownSchemes.Add("NLCD 2016", LandCoverCategories.NLCD2011Categories());
		landCoverLayerHelper.WellKnownSchemes.Add("Anderson II", LandCoverCategories.AndersonLevel2Categories());
		landCoverLayerHelper.WellKnownSchemes.Add("NOAA C-CAP", LandCoverCategories.CCAPCategories());
		return landCoverLayerHelper;
	}

	public static LandCoverLayerHelper DefaultSoils()
	{
		return new LandCoverLayerHelper
		{
			Title = "Soils Layer",
			DefaultFilenamePrefix = "Soils",
			WellKnownSchemes = { 
			{
				"Soils",
				LandCoverCategories.SoilCategories()
			} }
		};
	}

	public static LandCoverLayerHelper DefaultSedBedMat()
	{
		return new LandCoverLayerHelper
		{
			Title = "Sediment Bed Material Layer",
			DefaultFilenamePrefix = "Sediment Bed Material",
			WellKnownSchemes = { 
			{
				"Sediment Bed Material",
				LandCoverCategories.SoilCategories()
			} }
		};
	}

	public static LandCoverLayerHelper DefaultInfiltration()
	{
		return new LandCoverLayerHelper
		{
			Title = "Infiltration Layer",
			DefaultFilenamePrefix = "Infiltration"
		};
	}

	public static LandCoverLayerHelper InfiltrationDC()
	{
		LandCoverLayerHelper landCoverLayerHelper = new LandCoverLayerHelper();
		landCoverLayerHelper.Title = "Infiltration Layer " + "Deficit Constant".InParentheses();
		landCoverLayerHelper.DefaultFilenamePrefix = "InfiltrationDC";
		foreach (string allVariable in ParameterNames.InfiltrationVariables.DeficitConstantLoss.AllVariables.ToList())
		{
			landCoverLayerHelper.AddColumn(allVariable, allVariable, allVariable);
		}
		return landCoverLayerHelper;
	}

	public static LandCoverLayerHelper InfiltrationSCS()
	{
		LandCoverLayerHelper landCoverLayerHelper = new LandCoverLayerHelper();
		landCoverLayerHelper.Title = "Infiltration Layer " + "SCS Curve Number".InParentheses();
		landCoverLayerHelper.DefaultFilenamePrefix = "InfiltrationSCS";
		foreach (string allVariable in ParameterNames.InfiltrationVariables.SCSCurveNumber.AllVariables.ToList())
		{
			landCoverLayerHelper.AddColumn(allVariable, allVariable, allVariable);
		}
		return landCoverLayerHelper;
	}

	public static LandCoverLayerHelper InfiltrationGA()
	{
		LandCoverLayerHelper landCoverLayerHelper = new LandCoverLayerHelper();
		landCoverLayerHelper.Title = "Infiltration Layer " + "Green and Ampt".InParentheses();
		landCoverLayerHelper.DefaultFilenamePrefix = "InfiltrationGA";
		foreach (string allVariable in ParameterNames.InfiltrationVariables.GreenAmpt.AllVariables.ToList())
		{
			landCoverLayerHelper.AddColumn(allVariable, allVariable, allVariable);
		}
		return landCoverLayerHelper;
	}
}
