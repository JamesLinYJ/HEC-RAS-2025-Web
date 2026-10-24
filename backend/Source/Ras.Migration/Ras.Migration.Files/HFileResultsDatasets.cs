namespace Ras.Migration.Files;

public class HFileResultsDatasets
{
	public static class StableChannelDesign
	{
		public static class CopelandMethod
		{
			public static class StableDesign
			{
				public static string Base = CopelandMethod.Base + "Design ";

				public static string Discharge = Base + "Discharge";

				public static string LeftSideSlope = Base + "Left Side Slope";

				public static string RightSideSlope = Base + "Right Side Slope";

				public static string LeftSideSlopeEquation = Base + "Left Side Slope Equation";

				public static string RightSideSlopeEquation = Base + "Right Side Slope Equation";

				public static string LeftSideSlopeRoughnessValue = Base + "Left Side Slope Roughness Value";

				public static string RightSideSlopeRoughnessValue = Base + "Right Side Slope Roughness Value";
			}

			public static class StableSupply
			{
				public static string Base = CopelandMethod.Base + "Supply ";

				public static string SedimentConcentration = Base + "Sediment Concentration";

				public static string ChannelWidth = Base + "Channel Width";

				public static string ChannelBankHeight = Base + "Channel Bank Height";

				public static string ChannelSlope = Base + "Channel Slope";

				public static string LeftSideSlope = Base + "Left Side Slope";

				public static string RightSideSlope = Base + "Right Side Slope";

				public static string LeftSideSlopeEquation = Base + "Left Side Slope Equation";

				public static string RightSideSlopeEquation = Base + "Right Side Slope Equation";

				public static string LeftSideSlopeRoughnessValue = Base + "Left Side Slope Roughness Value";

				public static string RightSideSlopeRoughnessValue = Base + "Right Side Slope Roughness Value";
			}

			public static string Base = "Stable ";

			public static string SpecificGravity = Base + "Specific Gravity";

			public static string D84 = Base + "d84";

			public static string D50 = Base + "d50";

			public static string D16 = Base + "d16";

			public static string Temperature = Base + "Temperature";

			public static string ValleySlope = Base + "Valley Slope (optional)";

			public static string MedianChannelWidth = Base + "Median Channel Width (optional)";
		}

		public static class RegimeMethod
		{
			public static string Base = "Regime ";

			public static string D50 = Base + "d50";

			public static string Discharge = Base + "Discharge";

			public static string SedimentConcentration = Base + "Sediment Concentration";

			public static string SideFactor = Base + "Side Factor";

			public static string Temperature = Base + "Temperature";
		}

		public static class TractiveForceMethod
		{
			public static string Base = "Tractive Force ";

			public static string dLeft = Base + "d Left";

			public static string dBed = Base + "d Bed";

			public static string dRight = Base + "d Right";

			public static string Depth = Base + "Depth";

			public static string Discharge = Base + "Discharge";

			public static string AngleOfReposeBed = Base + "Angle of Repose Bed";

			public static string AngleOfReposeLeft = Base + "Angle of Repose Left";

			public static string AngleOfReposeRight = Base + "Angle of Repose Right";

			public static string RoughnessBed = Base + "Roughness Bed";

			public static string RoughnessLeft = Base + "Roughness Left";

			public static string RoughnessRight = Base + "Roughness Right";

			public static string Slope = Base + "Slope";

			public static string SpecificGravityBed = Base + "Specific Gravity Bed";

			public static string SpecificGravityLeft = Base + "Specific Gravity Left";

			public static string SpecificGravityRight = Base + "Specific Gravity Right";

			public static string SideSlopeLeft = Base + "Side Slope Left";

			public static string SideSlopeRight = Base + "Side Slope Right";

			public static string ShieldsParameterBed = Base + "Shield's Parameter Bed";

			public static string ShieldsParameterLeft = Base + "Shield's Parameter Left";

			public static string ShieldsParameterRight = Base + "Shield's Parameter Right";

			public static string Temperature = Base + "Temperature";

			public static string Width = Base + "Width";

			public static string BedEquation = Base + "Bed Equation";

			public static string LeftSideSlopeEquation = Base + "Left Side Slope Equation";

			public static string RightSideSlopeEquation = Base + "Right Side Slope Equation";

			public static string SolutionMethod = "Solution Method";
		}
	}

	public static class RatingCurveCalculator
	{
		public class Graphs
		{
			public class D85
			{
				public static string Base = Graphs.Base + "D85/";

				public static string UseLinearAxis = Base + "Use Linear Axis";
			}

			public static string Base = RatingCurveCalculator.Base + "Graphs/";
		}

		public class PiecewiseLinear
		{
			public static string Base = RatingCurveCalculator.Base + "PiecewiseLinear/";

			public static string RangeType = Base + "Range Type";

			public static string RangeMinimum = Base + "Range Minimum";

			public static string RangeMaximum = Base + "Range Maximum";

			public static string Knot = Base + "Knot";

			public static string NeedsRecalculation = Base + "Needs Recalculation";

			public static string LineResultData = Base + "Line Result Data";

			public static string Duan = Base + "Duan";

			public static string Ferguson = Base + "Ferguson";

			public static string NeedsInitialCalculation = Base + "Needs Initial Calculation";
		}

		public class StationarityAnalysis
		{
			public static string Base = RatingCurveCalculator.Base + "StationarityAnalysis/";

			public static string CurrentYear = Base + "Current Year";

			public static string SelectedOption = Base + "Selected Time Period";
		}

		public class CorrelationAnalysis
		{
			public static string Base = RatingCurveCalculator.Base + "CorrelationAnalysis/";

			public static string Mode = Base + "Mode";

			public static string Range = Base + "Cluster Range";

			public static string IncludeClusteredPoints = Base + "Include Clustered Points";
		}

		public class Hysteresis
		{
			public static string Base = RatingCurveCalculator.Base + "Hysteresis/";

			public static string CurrentYear = Base + "Current Year";
		}

		public class Data
		{
			public static string Base = RatingCurveCalculator.Base + "Data/";

			public static string DataEntries = Base + "Data Entries/";

			public static string FluxGradationDataEntries = Base + "Flux Gradation Data Entries";
		}

		public class Results
		{
			public static string Base = RatingCurveCalculator.Base + "Results/";

			public static string Flows = Base + "Flows";
		}

		public static string Base = "RatingCurveCalculator/";

		public static string UsingDuanCalculation = Base + "Using Duan Calculation";

		public static string IsDataImported = Base + "Is Data Imported";
	}

	public class RipRapCalculator
	{
		public class RipRap
		{
			public static string Base = RipRapCalculator.Base + "SizeAndThickness/";

			public static string IsSelectingSSGradation = Base + "Is Selecting SS Gradation";

			public static string ChannelType = Base + "Channel Type";

			public static string RockType = Base + "Rock Type";

			public static string SiteAlignment = Base + "Site Alignment";

			public static string SelectedProfile = Base + "Selected Profile";

			public static string CrossingXSRiver = Base + "Crossing XS River";

			public static string CrossingXSReach = Base + "Crossing XS Reach";

			public static string CrossingXSRS = Base + "Crossing XS River Station";

			public static string CrossingXSHydraulics = Base + "Crossing XS Hydraulics";

			public static string DesignXSRiver = Base + "Design XS River";

			public static string DesignXSReach = Base + "Design XS Reach";

			public static string DesignXSRS = Base + "Design XS River Station";

			public static string DesignXSHydraulics = Base + "Design XS Hydraulics";

			public static string RadiusOfCurvature = Base + "Radius of Curvature";

			public static string SideSlopeAngle = Base + "Side Slope Angle";

			public static string SafetyFactor = Base + "Safety Factor";

			public static string UnitWeight = Base + "Unit Weight";

			public static string AngleOfRepose = Base + "Angle of Repose";

			public static string ThicknessCoefficient = Base + "Thickness Coefficient";

			public static string ResultsDisplayType = Base + "Results Display Type";

			public static string K1Method = Base + "K1 Method";

			public static string IshbashTurbulence = Base + "Turbulence";

			public static string MaricopaTurbulence = Base + "Maricopa Turbulence";

			public static string MaricopaRockType = Base + "Maricopa Rock Type";

			public static string MaricopaMode = Base + "Maricopa Mode";
		}

		public class Scour
		{
			public static string Base = RipRapCalculator.Base + "Scour/";

			public static string SelectedProfile = Base + "Selected Profile";

			public static string CrossingXSRiver = Base + "Crossing XS River";

			public static string CrossingXSReach = Base + "Crossing XS Reach";

			public static string CrossingXSRS = Base + "Crossing XS River Station";

			public static string CrossingXSHydraulics = Base + "Crossing XS Hydraulics";

			public static string DesignXSRiver = Base + "Design XS River";

			public static string DesignXSReach = Base + "Design XS Reach";

			public static string DesignXSRS = Base + "Design XS River Station";

			public static string DesignXSHydraulics = Base + "Design XS Hydraulics";

			public static string RadiusOfCurvature = Base + "Radius of Curvature";

			public static string Temperature = Base + "Temperature";

			public static string D50 = Base + "D50";

			public static string DegreeOfBend = Base + "Degree of Bend";

			public static string UseLaceyForDepth = Base + "Use Lacey for Depth";

			public static string NeillBankFullQ = Base + "Neill Bankfull Q";

			public static string NeillBankFullWidth = Base + "Neill Bankfull Width";

			public static string NeillBankFullDepth = Base + "Neill Bankfull Depth";

			public static string NeillExponent = Base + "Neill Exponent";

			public static string BendMaynordEnabled = Base + "Bend Maynord Enabled";

			public static string BendZellerEnabled = Base + "Bend Zeller Enabled";

			public static string BendThorneEnabled = Base + "Bend Thorne Enabled";

			public static string BendUSACEEnabled = Base + "Bend USACE Enabled";

			public static string GeneralZellerEnabled = Base + "General Zeller Enabled";

			public static string GeneralNeilEnabled = Base + "General Neill Enabled";

			public static string GeneralLaceyEnabled = Base + "General Lacey Enabled";

			public static string GeneralUSBREnvEnabled = Base + "General USBR Env Enabled";

			public static string GeneralUSBRVelEnabled = Base + "General USBR Vel Enabled";

			public static string ToePointX = Base + "Toe Point X";

			public static string NeillProfile = Base + "Neill Profile";
		}

		public class Gradations
		{
			public static string Base = RipRapCalculator.Base + "Gradations/";

			public static string UserDefinedGradations = Base + "User Defined Gradations";

			public static string SelectedAvgGradation = Base + "Selected Avg Gradation";

			public static string SelectedSSGradation = Base + "Selected SS Gradation";
		}

		public static string Base = "RipRap/";
	}
}
