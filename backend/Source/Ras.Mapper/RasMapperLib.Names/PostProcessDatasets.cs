using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RasMapperLib.Names;

public class PostProcessDatasets
{
	public class Profile
	{
		public class CrossSections
		{
			private const string ProcessedData = "Processed Data";

			private const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/";

			private const string PREFIX = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)";

			public static string MaxDV()
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)/Maximum DV";
			}

			public static string MinDV()
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)/Minimum DV";
			}

			public static string MaxDVSq()
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)/Maximum DV-SQ";
			}

			public static string MinDVSq()
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)/Minimum DV-SQ";
			}

			public static string MaxShear()
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)/Maximum Shear";
			}

			public static string MinShear()
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Processed Data/Profile (Horizontal)/Minimum Shear";
			}
		}

		public class FlowAreas
		{
			private const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/";

			public static string MaxShear(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Maximum Shear";
			}

			public static List<string> MaxShear(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => MaxShear(mn)).ToList();
			}

			public static string MinShear(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Minimum Shear";
			}

			public static List<string> MinShear(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => MinShear(mn)).ToList();
			}

			public static string MaxDV(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Maximum DV";
			}

			public static List<string> MaxDV(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => MaxDV(mn)).ToList();
			}

			public static string MinDV(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Minimum DV";
			}

			public static List<string> MinDV(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => MinDV(mn)).ToList();
			}

			public static string MaxDVSq(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Maximum DV-SQ";
			}

			public static List<string> MaxDVSq(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => MaxDVSq(mn)).ToList();
			}

			public static string MinDVSq(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Minimum DV-SQ";
			}

			public static List<string> MinDVSq(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => MinDVSq(mn)).ToList();
			}

			public static string HydraulicArea(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Face Hydraulic Area";
			}

			public static List<string> HydraulicArea(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => HydraulicArea(mn)).ToList();
			}

			public static string HydraulicDepth(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Face Hydraulic Depth";
			}

			public static List<string> HydraulicDepth(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => HydraulicDepth(mn)).ToList();
			}

			public static string HydraulicRadius(string meshname)
			{
				return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshname + "/Processed Data/Profile (Horizontal)/Face Hydraulic Radius";
			}

			public static List<string> HydraulicRadius(IEnumerable<string> meshnames)
			{
				return meshnames?.Select([SpecialName] (string mn) => HydraulicRadius(mn)).ToList();
			}
		}

		private const string Orientation = "/Profile (Horizontal)";
	}

	public class TimeSeries
	{
		public class FlowAreas
		{
		}
	}

	private const string ProcessedData = "/Processed Data";
}
