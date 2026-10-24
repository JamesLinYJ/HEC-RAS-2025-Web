using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RasMapperLib.Mapping;

namespace RasMapperLib.Names;

public class ResultsDatasets
{
	public class Geometry
	{
		public class Attributes
		{
		}

		public const string Path = "Geometry/";
	}

	public class PlanData
	{
		public class BreachData
		{
			public const string Path = "Plan Data/Breach Data";
		}

		public class PlanInfo
		{
			public class Attributes
			{
				public const string ComputationTimeStep = "Computation Time Step";

				public const string GeometryName = "Geometry Name";

				public const string GeometryTitle = "Geometry Title";

				public const string OutputInterval = "Output Interval";

				public const string PlanFile = "Plan File";

				public const string PlanName = "Plan Name";

				public const string PlanShortID = "Plan ShortID";

				public const string TimeWindow = "Time Window";
			}

			public const string Path = "Plan Data/Plan Information";
		}

		public class PlanParameters
		{
			public const string Path = "Plan Data/Plan Parameters";
		}

		public const string Path = "Plan Data/";
	}

	public class Unsteady
	{
		public class SummaryOutput
		{
			public class CrossSections
			{
				public class MaxWaterSurface
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Water Surface";

					public const int WSELRow = 0;

					public const int CorrespondingFlowRow = 1;

					public const int CorrespondingTimeRow = 2;
				}

				public class MinWaterSurface
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Water Surface";

					public const int WSELRow = 0;

					public const int CorrespondingFlowRow = 1;

					public const int CorrespondingTimeRow = 2;
				}

				public class MaxFlow
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Flow";

					public const int MaxFlowRow = 0;

					public const int CorrespondingStageRow = 1;

					public const int CorrespondingTimeRow = 2;
				}

				public class MinFlow
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Flow";

					public const int MinFlowRow = 0;

					public const int CorrespondingStageRow = 1;

					public const int CorrespondingTimeRow = 2;
				}

				public class MaxChannelVelocity
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Channel Velocity";

					public const int VelocityRow = 0;

					public const int CorrespondingStageRow = 1;

					public const int CorrespondingFlowRow = 2;

					public const int CorrespondingTimeRow = 3;
				}

				public class MinChannelVelocity
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Channel Velocity";

					public const int VelocityRow = 0;

					public const int CorrespondingStageRow = 1;

					public const int CorrespondingFlowRow = 2;

					public const int CorrespondingTimeRow = 3;
				}

				public class MaxChannelSubsectionVelocity
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Maximum Velocity Flow Distribution";
				}

				public class MinChannelSubsectionVelocity
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/Minimum Velocity Flow Distribution";
				}

				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Cross Sections/";
			}

			public class FlowAreas
			{
				public class MaxWaterSurface
				{
					public const int WSELRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Maximum Water Surface";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public class MinWaterSurface
				{
					public const int WSELRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Minimum Water Surface";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public class MaxShear
				{
					public const int ShearRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Maximum Face Shear Stress";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public class MaxFaceVelocity
				{
					public const int VelocityRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Maximum Face Velocity";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public class MinFaceVelocity
				{
					public const int VelocityRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Minimum Face Velocity";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public class MaxFaceDV
				{
					public const int DVRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Maximum Face Depth Velocity";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public class MaxFaceDVSq
				{
					public const int DVSqRow = 0;

					public const int TimeRow = 1;

					public static string Name(string meshName)
					{
						return "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/" + meshName + "/Maximum Face Depth Velocity^2";
					}

					public static List<string> Name(IList<string> meshNames)
					{
						return meshNames?.Select([SpecialName] (string n) => Name(n)).ToList();
					}
				}

				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/2D Flow Areas/";
			}

			public class StorageAreas
			{
				public class MaxWaterSurface
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Maximum Water Surface";

					public const int WSELRow = 0;

					public const int CorrespondingTimeRow = 1;
				}

				public class MinWaterSurface
				{
					public const string Name = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/Minimum Water Surface";

					public const int WSELRow = 0;

					public const int CorrespondingTimeRow = 1;
				}

				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/Storage Areas/";
			}

			public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Summary Output/";

			private const string MaxWS = "Maximum Water Surface";

			private const string MinWS = "Minimum Water Surface";

			private const string MaxFaceVel = "Maximum Face Velocity";

			private const string MinFaceVel = "Minimum Face Velocity";

			private const string MaxChannelVel = "Maximum Channel Velocity";

			private const string MinChannelVel = "Minimum Channel Velocity";

			private const string MaxFlowVar = "Maximum Flow";

			private const string MinFlowVar = "Minimum Flow";

			private const string MaxShearVar = "Maximum Face Shear Stress";

			private const string MaxDVVar = "Maximum Face Depth Velocity";

			private const string MaxDVSqVar = "Maximum Face Depth Velocity^2";

			private const string MaxVelFlowDistrVar = "Maximum Velocity Flow Distribution";

			private const string MinVelFlowDistrVar = "Minimum Velocity Flow Distribution";
		}

		public class TimeSeriesOutput
		{
			public class CrossSections
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/";

				public const string WaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Water Surface";

				public const string Flow = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Flow";

				public const string VelocityTotal = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Velocity Total";

				public const string VelocityChannel = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/Velocity Channel";

				public static string ArbitraryVar(string varname)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Cross Sections/" + varname;
				}
			}

			public class FlowAreas
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/";

				public static string WaterSurface(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Water Surface";
				}

				public static List<string> WaterSurface(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => WaterSurface(name)).ToList();
				}

				public static string ComputeTimeStep(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Time Step";
				}

				public static List<string> ComputeTimeStep(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => ComputeTimeStep(name)).ToList();
				}

				public static string FaceLaplacianU(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Laplacian Scalar U";
				}

				public static string FaceLaplacianV(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Laplacian Scalar V";
				}

				public static List<string> FaceLaplacianU(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => FaceLaplacianU(name)).ToList();
				}

				public static List<string> FaceLaplacianV(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => FaceLaplacianV(name)).ToList();
				}

				public static string Depth(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Depth";
				}

				public static List<string> Depth(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => Depth(name)).ToList();
				}

				public static string FaceVelocity(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Face Velocity";
				}

				public static List<string> FaceVelocity(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => FaceVelocity(name)).ToList();
				}

				public static string FaceFlow(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Face Q";
				}

				public static string FacePointVelocityX(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Node X Vel";
				}

				public static List<string> FacePointVelocityX(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => FacePointVelocityX(name)).ToList();
				}

				public static string FacePointVelocityY(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Node Y Vel";
				}

				public static List<string> FacePointVelocityY(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => FacePointVelocityY(name)).ToList();
				}

				public static string ShearStress(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Face Shear Stress";
				}

				public static List<string> ShearStress(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => ShearStress(name)).ToList();
				}

				public static string Flow(string meshName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Flow";
				}

				public static List<string> Flow(IList<string> meshNames)
				{
					return meshNames?.Select([SpecialName] (string name) => Flow(name)).ToList();
				}

				public static string ArbitraryVar(string meshname, string varname)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshname + "/" + varname;
				}

				public static List<string> ArbitraryVar(IList<string> meshNames, string varName)
				{
					return meshNames?.Select([SpecialName] (string name) => ArbitraryVar(name, varName)).ToList();
				}

				public static string BoundaryConditionBase_Old(string meshName, string bcLineName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/2D Flow Areas/" + meshName + "/Boundary Conditions/" + bcLineName;
				}

				public static string FlowBoundaryConditionPerCell_Old(string meshName, string bcLineName)
				{
					return BoundaryConditionBase_Old(meshName, bcLineName) + " - Flow";
				}

				public static string StageBoundaryConditionPerCell_Old(string meshName, string bcLineName)
				{
					return BoundaryConditionBase_Old(meshName, bcLineName) + " - Stage";
				}

				public static string BoundaryConditionBase(string bcLineName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Boundary Conditions/" + bcLineName;
				}

				public static string FlowBoundaryConditionPerCell(string bcLineName)
				{
					return BoundaryConditionBase(bcLineName) + " - Flow per Cell";
				}

				public static string StageBoundaryConditionPerCell(string bcLineName)
				{
					return BoundaryConditionBase(bcLineName) + " - Stage per Cell";
				}
			}

			public class LateralStructures
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Lateral Structures/";

				public static string FlowSegments(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Lateral Structures/" + lsName + "/HW TW Segments/Flow";
				}

				public static string GroupName(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Lateral Structures/" + lsName;
				}

				public static string LeveeBreaching(string lsName)
				{
					return GroupName(lsName) + "/Levee Breaching";
				}

				public static string StructureVariables(string lsName)
				{
					return GroupName(lsName) + "/Structure Variables";
				}

				public static string BreachingVariables(string lsName)
				{
					return GroupName(lsName) + "/Breaching Variables";
				}

				public static string CulvertGroup(string lsName, string cgName)
				{
					return GroupName(lsName) + "/Culvert Groups/" + cgName;
				}

				public static string GateGroup(string lsName, string ggName)
				{
					return GroupName(lsName) + "/Gate Groups/" + ggName;
				}

				public static string WeirVariables(string lsName)
				{
					return GroupName(lsName) + "/Weir Variables";
				}
			}

			public class Bridges
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Bridges/";

				public static string GroupName(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Bridges/" + lsName;
				}

				public static string BridgeVariables(string lsName)
				{
					return GroupName(lsName) + "/Bridge Variables";
				}
			}

			public class Culverts
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Culverts/";

				public static string GroupName(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Culverts/" + lsName;
				}

				public static string CulvertVariables(string lsName)
				{
					return GroupName(lsName) + "/Culvert Variables";
				}
			}

			public class MultipleOpenings
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Multiple Openings/";

				public static string GroupName(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Multiple Openings/" + lsName;
				}

				public static string MultipleOpeningsVariables(string lsName)
				{
					return GroupName(lsName) + "/Multiple Opening Variables";
				}
			}

			public class InlineStructures
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Inline Structures/";

				public static string GroupName(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Inline Structures/" + lsName;
				}

				public static string LeveeBreaching(string lsName)
				{
					return GroupName(lsName) + "/Levee Breaching";
				}

				public static string StructureVariables(string lsName)
				{
					return GroupName(lsName) + "/Structure Variables";
				}

				public static string BreachingVariables(string lsName)
				{
					return GroupName(lsName) + "/Breaching Variables";
				}

				public static string CulvertGroup(string lsName, string cgName)
				{
					return GroupName(lsName) + "/Culvert Groups/" + cgName;
				}

				public static string GateGroup(string lsName, string ggName)
				{
					return GroupName(lsName) + "/Gate Groups/" + ggName;
				}
			}

			public class SA2DConnections
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/SA 2D Area Conn/";

				public static string FlowSegments(string meshName, string connName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/SA 2D Area Conn/" + meshName + " " + connName + "/HW TW Segments/Flow";
				}

				public static string FlowSegments(string connName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/SA 2D Area Conn/" + connName + "/HW TW Segments/Flow";
				}

				public static string GroupName(string meshName, string connName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/SA 2D Area Conn/" + meshName + " " + connName;
				}

				public static string GroupName(string connName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/SA 2D Area Conn/" + connName;
				}

				public static string LeveeBreaching(string meshName, string connName)
				{
					return GroupName(meshName, connName) + "/Levee Breaching";
				}

				public static string LeveeBreaching(string connName)
				{
					return GroupName(connName) + "/Levee Breaching";
				}

				public static string StructureVariables(string meshName, string connName)
				{
					return GroupName(meshName, connName) + "/Structure Variables";
				}

				public static string StructureVariables(string connName)
				{
					return GroupName(connName) + "/Structure Variables";
				}

				public static string BreachingVariables(string meshName, string connName)
				{
					return GroupName(meshName, connName) + "/Breaching Variables";
				}

				public static string BreachingVariables(string connName)
				{
					return GroupName(connName) + "/Breaching Variables";
				}

				public static string WeirVariables(string meshName, string connName)
				{
					return GroupName(meshName, connName) + "/Weir Variables";
				}

				public static string WeirVariables(string connName)
				{
					return GroupName(connName) + "/Weir Variables";
				}

				public static string CulvertGroup(string connName, string cgName)
				{
					return GroupName(connName) + "/Culvert Groups/" + cgName;
				}

				public static string CulvertGroup(string meshName, string connName, string cgName)
				{
					return GroupName(meshName, connName) + "/Culvert Groups/" + cgName;
				}

				public static string GateGroup(string connName, string ggName)
				{
					return GroupName(connName) + "/Gate Groups/" + ggName;
				}

				public static string GateGroup(string meshName, string connName, string ggName)
				{
					return GroupName(meshName, connName) + "/Gate Groups/" + ggName;
				}
			}

			public class StorageAreas
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/";

				public const string WaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Water Surface";

				public const string Flow = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/Flow";

				public static string ArbitraryVar(string varname)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Storage Areas/" + varname;
				}

				public static string StorageAreaVariables(string saName)
				{
					return ArbitraryVar(saName) + "/Storage Area Variables";
				}
			}

			public class ReferencePoints
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Points/";

				public const string WaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Points/Water Surface";
			}

			public class ReferenceLines
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/";

				public const string WaterSurface = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/Water Surface";

				public const string Flow = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/Flow";

				public static string VariableName(string varName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Reference Lines/" + varName;
				}
			}

			public class PumpStations
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Pumping Stations/";

				public static string GroupName(string lsName)
				{
					return "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/Pumping Stations/" + lsName;
				}

				public static string StructureVariables(string lsName)
				{
					return GroupName(lsName) + "/Structure Variables";
				}
			}

			public const string BASE = "Results/Unsteady/Output/Output Blocks/Base Output/Unsteady Time Series/";
		}

		public class Attributes
		{
			public const string PlanTitle = "Plan Title";

			public const string ProgramName = "Program Name";

			public const string ProgramVersion = "Program Version";

			public const string ProjectFileName = "Project File Name";

			public const string ProjectTitle = "ProjectTitle";

			public const string ShortID = "Short ID";

			public const string SimTimeWindow = "Simulation Time Window";

			public const string TypeOfRun = "Type of Run";
		}

		public class Summary
		{
			public class Attributes
			{
				public const string MaxWSELErrorFP32 = "Maximum WSEL Error";

				public const string RunTimeWindow = "Run Time Window";

				public const string Solution = "Solution";

				public const string TimeSlnWentUnstable = "Time Solution Went Unstable";

				public const string TimeStampSolnWentUnstable = "Time Stamp Solution Went Unstable";

				public const string VolAccounting = "Vol Accounting";

				public const string VolAccounting2DBDry = "Vol Accounting 2D Bdry";

				public const string VolAccountingCellEnd = "Vol Accounting Cell End";

				public const string VolAccountingCellStart = "Vol Accounting Cell Start";

				public const string VolAccountingDSOutflow = "Vol Accounting DS Outflow";

				public const string VolAccountingErr = "Vol Accounting Error";

				public const string VolAccountingErrPct = "Vol Accounting Error Percentage";

				public const string VolAccountingGroundwater = "Vol Accounting Groundwater";

				public const string VolAccountingLSPumpDiv = "Vol Accounting LS, Pump Div";

				public const string VolAccountingLatHydro = "Vol Accounting Lat Hydro";

				public const string VolAccountingUSInflow = "Vol Accounting US Inflow";
			}

			public const string Path = "Results/Unsteady/Summary/";
		}

		public const string Path = "Results/Unsteady/";

		public const string OutputBlockBase = "Results/Unsteady/Output/Output Blocks/";

		public const string BaseOutput = "Results/Unsteady/Output/Output Blocks/Base Output/";

		public const string Encroachments = "Plan Data/Fixed Encroachments";
	}

	public class Steady
	{
		public class CrossSections
		{
			public const string BASE = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/";

			private const string AdditionalVarFolder = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/";

			public const string LeftEncroachmentStations = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Encroachment Station Left";

			public const string RightEncroachmentStations = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Encroachment Station Right";

			public const string HydraulicDepthTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Hydraulic Depth Total";

			public const string HydraulicDepthChannel = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Hydraulic Depth Channel";

			public const string VelocityTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Velocity Total";

			public const string VelocityChannel = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Velocity Channel";

			public const string TopWidthTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Top Width Total";

			public const string TopWidthChannel = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Top Width Channel";

			public const string ManningNTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Manning n Total";

			public const string ManningNChannel = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Manning n Channel";

			public const string WettedPerimeterTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Wetted Perimeter Total";

			public const string WettedPerimeterChannel = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Wetted Perimeter Channel";

			public const string HydraulicRadiusTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Hydraulic Radius Total";

			public const string HydraulicRadiusChannel = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Hydraulic Radius Channel";

			public const string EGSlope = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/EG Slope";

			public const string MaximumDepthTotal = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Additional Variables/Maximum Depth Total";

			public const string WaterSurface = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";

			public const string Flow = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow";
		}

		public class StorageAreas
		{
			public const string BASE = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Storage Areas/";

			public const string WaterSurface = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Storage Areas/Water Surface";
		}

		public class Summary
		{
			public const string Path = "Results/Steady/Summary/";
		}

		public class Output
		{
			public const string Path = "Results/Steady/Output/";
		}

		public const string Path = "Results/Steady/";

		public const string OutputBlockBase = "Results/Steady/Output/Output Blocks/";

		public const string BaseOutput = "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/";

		public static string GetDatasetName1D(MapTypes mt)
		{
			if (mt == MapTypes.Elevation)
			{
				return "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Water Surface";
			}
			if (mt == MapTypes.Flow)
			{
				return "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Cross Sections/Flow";
			}
			throw new NotImplementedException();
		}

		public static string GetDatasetNameSA(MapTypes mt)
		{
			if (mt == MapTypes.Elevation)
			{
				return "Results/Steady/Output/Output Blocks/Base Output/Steady Profiles/Storage Areas/Water Surface";
			}
			throw new NotImplementedException();
		}
	}

	public class Quasi
	{
		public class CrossSections
		{
			public const string BASE = "Results/Sediment/Output Blocks/Sediment/Sediment Time Series/Cross Sections/";

			public static string GetDataset(string dsetName)
			{
				return "Results/Sediment/Output Blocks/Sediment/Sediment Time Series/Cross Sections/" + dsetName;
			}

			public static string GetFlowDataset()
			{
				return GetDataset("Flow");
			}

			public static string GetWSELDataset()
			{
				return GetDataset("Water Surface");
			}

			public static string GetVelocityDataset()
			{
				return GetDataset("Velocity");
			}
		}

		public const string Path = "Results/Sediment/";

		public const string BaseOutput = "Results/Sediment/Output Blocks/Sediment/Sediment Time Series/";

		public const string OutputBlockBase = "Results/Sediment/Output Blocks";
	}

	public class UnsteadySedimentBed
	{
		public class TimeSeriesOutput
		{
			public class FlowAreas
			{
				public const string BASE = "Results/Unsteady/Output/Output Blocks/Sediment Bed/Unsteady Time Series/2D Flow Areas/";

				public static string GetDataset(string meshName, string dsetName)
				{
					return "Results/Unsteady/Output/Output Blocks/Sediment Bed/Unsteady Time Series/2D Flow Areas/" + meshName + "/" + dsetName;
				}
			}

			public const string BASE = "Results/Unsteady/Output/Output Blocks/Sediment Bed/Unsteady Time Series/";
		}

		public const string Path = "Results/Unsteady/";

		public const string OutputBlockBase = "Output/Output Blocks/";

		public const string BaseOutput = "Results/Unsteady/Output/Output Blocks/Sediment Bed/";
	}

	public class UnsteadySediment
	{
		public class CrossSections
		{
			public const string BASE = "Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series/Cross Sections/";

			public static string GetDataset(string dsetName)
			{
				return "Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series/Cross Sections/" + dsetName;
			}

			public static string GetFlowDataset()
			{
				return GetDataset("Flow");
			}

			public static string GetWSELDataset()
			{
				return GetDataset("Water Surface");
			}

			public static string GetVelocityDataset()
			{
				return GetDataset("Velocity");
			}
		}

		public const string Path = "Results/Unsteady/";

		public const string BaseOutput = "Results/Unsteady/Output/Output Blocks/Sediment/Sediment Time Series/";
	}

	public class Uncertainty
	{
		private static string BASE = "Results/Uncertainty Analysis";

		public static string CrossSections = BASE + "/Cross Sections";
	}
}
