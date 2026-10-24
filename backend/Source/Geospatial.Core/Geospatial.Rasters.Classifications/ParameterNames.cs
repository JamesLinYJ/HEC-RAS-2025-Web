namespace Geospatial.Rasters.Classifications;

public static class ParameterNames
{
	public static class InfiltrationVariables
	{
		public static class DeficitConstantLoss
		{
			public const string MaximumDeficit = "Maximum Deficit";

			public const string InitialDeficit = "Initial Deficit";

			public const string PotentialPercolationRate = "Potential Percolation Rate";

			public static readonly string[] AllVariables = new string[3] { "Maximum Deficit", "Initial Deficit", "Potential Percolation Rate" };

			public static readonly string[] AllVariablesUnits = new string[3] { MaximumDeficitUnits, InitialDeficitUnits, PotentialPercolationRateUnits };

			public static readonly string[] MandatoryVariables = new string[3] { "Maximum Deficit", "Initial Deficit", "Potential Percolation Rate" };

			public static readonly string[] MandatoryVariablesUnits = new string[3] { MaximumDeficitUnits, InitialDeficitUnits, PotentialPercolationRateUnits };

			public static string MaximumDeficitUnits => "";

			public static string InitialDeficitUnits => "";

			public static string PotentialPercolationRateUnits => "";
		}

		public static class SCSCurveNumber
		{
			public const string CurveNumber = "Curve Number";

			public const string InitialAbstractionRatio = "Abstraction Ratio";

			public const string MinInfilRate = "Minimum Infiltration Rate";

			public static readonly string[] AllVariables = new string[3] { "Curve Number", "Abstraction Ratio", "Minimum Infiltration Rate" };

			public static readonly string[] AllVariablesUnits = new string[3] { "", "", MinInfilRateUnits };

			public static readonly string[] MandatoryVariables = new string[2] { "Curve Number", "Abstraction Ratio" };

			public static readonly string[] MandatoryVariablesUnits = new string[2] { "", "" };

			public static string MinInfilRateUnits => "";
		}

		public static class GreenAmpt
		{
			public const string WettingFrontSuction = "Wetting Front Suction";

			public const string SaturatedHydraulicConductivity = "Saturated Hydraulic Conductivity";

			public const string InitialSoilWaterContent = "Initial Soil Water Content";

			public const string SaturatedSoilWaterContent = "Saturated Soil Water Content";

			public const string ResidualSoilWaterContent = "Residual Soil Water Content";

			public const string PoreSizeDistribution = "Pore Size Distribution Index";

			public static readonly string[] AllVariables = new string[6] { "Wetting Front Suction", "Saturated Hydraulic Conductivity", "Initial Soil Water Content", "Saturated Soil Water Content", "Residual Soil Water Content", "Pore Size Distribution Index" };

			public static readonly string[] AllVariablesUnits = new string[6] { WettingFrontSuctionUnits, SaturatedHydraulicConductivityUnits, "", "", "", "" };

			public static readonly string[] MandatoryVariablesBase = new string[4] { "Wetting Front Suction", "Saturated Hydraulic Conductivity", "Initial Soil Water Content", "Saturated Soil Water Content" };

			public static readonly string[] MandatoryVariablesBaseUnits = new string[4] { WettingFrontSuctionUnits, SaturatedHydraulicConductivityUnits, "", "" };

			public static readonly string[] MandatoryVariablesGASR = new string[4] { "Wetting Front Suction", "Saturated Hydraulic Conductivity", "Initial Soil Water Content", "Saturated Soil Water Content" };

			public static readonly string[] MandatoryVariablesGASRUnits = new string[4] { WettingFrontSuctionUnits, SaturatedHydraulicConductivityUnits, "", "" };

			public static readonly string[] MandatoryVariablesGAR = new string[6] { "Wetting Front Suction", "Saturated Hydraulic Conductivity", "Initial Soil Water Content", "Saturated Soil Water Content", "Residual Soil Water Content", "Pore Size Distribution Index" };

			public static readonly string[] MandatoryVariablesGARUnits = new string[6] { WettingFrontSuctionUnits, SaturatedHydraulicConductivityUnits, "", "", "", "" };

			public static string WettingFrontSuctionUnits => "";

			public static string SaturatedHydraulicConductivityUnits => "";
		}

		public const string PercentImpervious = "Percent Impervious";
	}

	public const string ManningsN = "ManningsN";
}
