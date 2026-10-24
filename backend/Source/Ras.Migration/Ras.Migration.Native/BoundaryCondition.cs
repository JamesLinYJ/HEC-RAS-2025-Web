using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial.Rasters;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility.Extensions;
using Utility.Files;
using Utility.Progress;

namespace Ras.Migration.Native;

public class BoundaryCondition
{
	private const StringSplitOptions RM = StringSplitOptions.RemoveEmptyEntries;

	public Project Project { get; set; }

	public string Filename { get; private set; }

	public string H5Filename => Filename + ".hdf";

	public string Name { get; set; }

	public string FlowExtension { get; private set; }

	public string Version { get; set; }

	public string Description { get; set; }

	public PlanType Type { get; private set; }

	public bool UseRestart { get; private set; }

	public string RestartFilename { get; private set; }

	public bool UsePriorWaterSurface { get; private set; }

	public string PriorWaterSurfaceFilename { get; private set; }

	public string PriorWaterSurfaceProfile { get; private set; }

	public List<RRSValue> InitialFlowLocations { get; private set; } = new List<RRSValue>();

	public List<RRSValue> InitialRRSElevations { get; private set; } = new List<RRSValue>();

	public List<NamedValue> InitialStorageAreaElevations { get; } = new List<NamedValue>();

	public List<NamedValue> InitialConditionPointElevations { get; } = new List<NamedValue>();

	public List<Boundary> Boundaries { get; } = new List<Boundary>();

	public List<MetStation> MetStations { get; } = new List<MetStation>();

	public RasterDefinition MetRasterDefinition { get; private set; }

	public string PrecipitationMode { get; private set; }

	public string WindMode { get; private set; }

	public string AirDensityMode { get; private set; }

	public List<MetBC> MetBCs { get; private set; } = new List<MetBC>();

	public bool KeepSAConstant { get; private set; }

	public List<RRS_HWM> HighWaterMarks { get; } = new List<RRS_HWM>();

	public NonNewtonianParams NonNewtonian { get; } = new NonNewtonianParams();

	public FileState FileStatus { get; private set; }

	private static FileState BAD => FileState.Malformed;

	private BoundaryCondition()
	{
	}

	public static bool IsValidFilename(string flowFilename)
	{
		string extension = Path.GetExtension(flowFilename);
		if (extension.Length != 4)
		{
			return false;
		}
		char c = extension[1];
		if (c != 'u' && c != 'f' && c != 'q')
		{
			return false;
		}
		if (!char.IsNumber(extension[2]))
		{
			return false;
		}
		if (!char.IsNumber(extension[3]))
		{
			return false;
		}
		return true;
	}

	public static bool TryParse(string flowFilename, out BoundaryCondition bc, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		bc = new BoundaryCondition();
		bc.Filename = flowFilename;
		bc.FlowExtension = Path.GetExtension(bc.Filename).Substring(1);
		try
		{
			if (bc.FlowExtension.First() == 'f')
			{
				bc.Type = PlanType.Steady;
			}
			else if (bc.FlowExtension.First() == 'u')
			{
				bc.Type = PlanType.Unsteady;
			}
			else if (bc.FlowExtension.First() == 'q')
			{
				bc.Type = PlanType.QuasiUnsteady;
			}
			else
			{
				bc.Type = PlanType.Unknown;
			}
			if (!File.Exists(bc.Filename))
			{
				bc.FileStatus = FileState.DoesNotExist;
				return false;
			}
			if (bc.Type == PlanType.QuasiUnsteady)
			{
				bc.Name = "UNKNOWN (Quasi Unsteady)";
				bc.Version = "UNKNOWN";
				return false;
			}
			if (bc.Type == PlanType.Unsteady)
			{
				bc.FileStatus = bc.ParseInternalUnsteady(bc.Filename, pr);
				if (bc.FileStatus == FileState.Good)
				{
					return true;
				}
				return false;
			}
			if (bc.Type == PlanType.Steady)
			{
				bc.FileStatus = bc.ParseInternalSteady(bc.Filename, pr);
				if (bc.FileStatus == FileState.Good)
				{
					return true;
				}
				return false;
			}
			bc.FileStatus = FileState.Unknown;
			return false;
		}
		catch
		{
			bc.FileStatus = FileState.Malformed;
			return false;
		}
	}

	private FileState ParseInternalUnsteady(string fn, ProgressReporter pr)
	{
		Parser parser = new Parser(File.ReadAllLines(fn), pr);
		if (parser.AssertKey("Flow Title", out string textValue))
		{
			Name = textValue;
			pr.ReportMessage("Title: " + Name);
			if (parser.AssertKey("Program Version", "Version", out var version))
			{
				Version = version;
				pr.ReportMessage("Version: " + Version);
				if (parser.OptionalGuardedMultiline("BEGIN FILE DESCRIPTION:", "END FILE DESCRIPTION:", out var description))
				{
					Description = description;
					if (parser.CheckKey("Use Restart", out bool useRestart))
					{
						UseRestart = useRestart;
					}
					if (parser.CheckKey("Restart Filename", out string restartFilename))
					{
						RestartFilename = restartFilename;
					}
					if (parser.EOF)
					{
						return FileState.Good;
					}
					if (parser.CheckKey("Use Prior WS", out bool usePriorWaterSurface))
					{
						UsePriorWaterSurface = usePriorWaterSurface;
					}
					if (parser.CheckKey("Use Results", out usePriorWaterSurface))
					{
						UsePriorWaterSurface = usePriorWaterSurface;
					}
					if (parser.CheckKey("Prior WS Filename", out string priorWaterSurfaceFilename))
					{
						PriorWaterSurfaceFilename = priorWaterSurfaceFilename;
					}
					if (parser.CheckKey("Prior WS Profile", out string priorWaterSurfaceProfile))
					{
						PriorWaterSurfaceProfile = priorWaterSurfaceProfile;
					}
					string textValue2;
					while (parser.CheckKey("Initial Flow Loc", out textValue2))
					{
						if (ParseAsRRSFloatPair(textValue2, pr, out var rv))
						{
							InitialFlowLocations.Add(rv);
							continue;
						}
						return BAD;
					}
					string textValue3;
					while (parser.CheckKey("Initial Storage Elev", out textValue3))
					{
						if (ParseAsNamedValue(textValue3, pr, out var nv))
						{
							InitialStorageAreaElevations.Add(nv);
							continue;
						}
						return BAD;
					}
					if (parser.CheckKey("Keep SA Constant", out bool keepSAConstant))
					{
						KeepSAConstant = keepSAConstant;
					}
					string textValue4;
					while (parser.CheckKey("IC Point Elev", out textValue4))
					{
						if (ParseAsNamedValue(textValue4, pr, out var nv2))
						{
							InitialConditionPointElevations.Add(nv2);
							continue;
						}
						return BAD;
					}
					string textValue5;
					while (parser.CheckKey("Initial RRR Elev", out textValue5))
					{
						if (ParseAsRRSFloatPair(textValue5, pr, out var rv2))
						{
							InitialRRSElevations.Add(rv2);
							continue;
						}
						return BAD;
					}
					if (parser.EOF)
					{
						return FileState.Good;
					}
					int lineIndex;
					do
					{
						lineIndex = parser.LineIndex;
						string textValue6;
						string textValue7;
						while (parser.CheckKey("Boundary Location", out textValue6))
						{
							Boundary boundary = new Boundary();
							Boundaries.Add(boundary);
							string[] data = textValue6.Split(',', StringSplitOptions.RemoveEmptyEntries);
							boundary.Location = new BoundaryLocation(data);
							if (parser.OptionalGuardedMultiline("BEGIN BOUNDARY DESCRIPTION:", "END BOUNDARY DESCRIPTION:", out var description2))
							{
								boundary.Description = description2;
							}
							if (parser.CheckKey("Boundary Name", out string textValue8))
							{
								boundary.Name = textValue8;
							}
							if (parser.CheckKey("Interval", out string timeInterval))
							{
								boundary.TimeInterval = timeInterval;
							}
							if (parser.EOF)
							{
								return FileState.Good;
							}
							if (!parser.GetKVP(out var key, out var localValue))
							{
								pr.ReportError("Expected BC Key-Value-Pair.");
								return BAD;
							}
							float computed;
							string hydroDssFile;
							string hydroDssPath;
							string hydroDssPath2;
							bool hydroUseDss;
							bool condition;
							bool hydroUseAbsTime;
							string hydroFixedStartDt;
							bool hydroIsCritBoundary;
							float hydroCritBoundaryFlow;
							switch (key)
							{
							case "Boundary Location":
								parser.UndoLine();
								break;
							case "Stage Hydrograph":
							{
								boundary.HydrographType = HydrographType.Stage;
								if (!int.TryParse(localValue, out var result8))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLines(result8, out var data13))
								{
									return BAD;
								}
								boundary.Stage = data13.ToArray();
								if (parser.CheckKey("Stage Hydrograph Use Initial Stage", out bool stageHydrographUseInitialStage))
								{
									boundary.StageHydrographUseInitialStage = stageHydrographUseInitialStage;
								}
								if (parser.CheckKey("Stage Hydrograph TW Check", out bool stageHydrographTailwaterCheck))
								{
									boundary.StageHydrographTailwaterCheck = stageHydrographTailwaterCheck;
								}
								goto IL_0c65;
							}
							case "Flow Hydrograph":
							{
								boundary.HydrographType = HydrographType.Flow;
								if (!int.TryParse(localValue, out var result13))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLines(result13, out var data15))
								{
									return BAD;
								}
								boundary.Flow = data15.ToArray();
								if (parser.CheckKey("Flow Hydrograph Inital WS", out float flowHydroInitialWS))
								{
									boundary.FlowHydroInitialWS = flowHydroInitialWS;
								}
								if (parser.CheckKey("Stage Hydrograph TW Check", out bool stageHydrographTailwaterCheck2))
								{
									boundary.StageHydrographTailwaterCheck = stageHydrographTailwaterCheck2;
								}
								goto IL_0c65;
							}
							case "Stage and Flow Hydrograph":
							{
								boundary.HydrographType = HydrographType.StageAndFlow;
								if (!int.TryParse(localValue, out var result9))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLinesXY(result9, out var xvals, out var yvals))
								{
									return BAD;
								}
								boundary.Stage = xvals;
								boundary.Flow = yvals;
								goto IL_0c65;
							}
							case "Rating Curve":
							{
								boundary.HydrographType = HydrographType.RatingCurve;
								if (!int.TryParse(localValue, out var result10))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLinesXY(result10, out var xvals2, out var yvals2))
								{
									return BAD;
								}
								boundary.Stage = xvals2;
								boundary.Flow = yvals2;
								goto IL_0c65;
							}
							case "Lateral Inflow Hydrograph":
							{
								boundary.HydrographType = HydrographType.LateralInflow;
								if (!int.TryParse(localValue, out var result5))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLines(result5, out var data10))
								{
									return BAD;
								}
								boundary.Flow = data10.ToArray();
								goto IL_0c65;
							}
							case "Uniform Lateral Inflow Hydrograph":
							{
								boundary.HydrographType = HydrographType.UniformLateralInflow;
								if (!int.TryParse(localValue, out var result7))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLines(result7, out var data12))
								{
									return BAD;
								}
								boundary.Flow = data12.ToArray();
								goto IL_0c65;
							}
							case "Ground Water Interflow":
							{
								boundary.HydrographType = HydrographType.GroundWaterInterflow;
								if (!int.TryParse(localValue, out var result3))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLines(result3, out var data6))
								{
									return BAD;
								}
								boundary.Stage = data6.ToArray();
								parser.CheckKey("Ground Water Darcy K", out float _);
								parser.CheckKey("Ground Water Darcy K/day", out float _);
								parser.CheckKey("Ground Water Darcy Distance", out float _);
								if (parser.CheckKey("Ground Water Interflow K Time Series", out int ordinal))
								{
									if (!parser.ReadFloatLines(ordinal, out var _))
									{
										pr.ReportError("Unexpected error reading Ground Water k values");
										return BAD;
									}
									if (!parser.ReadDoubleLines(ordinal, out var _))
									{
										return BAD;
									}
								}
								goto IL_0c65;
							}
							case "Observed Stage and Flow Hydrograph":
							{
								boundary.HydrographType = HydrographType.ObservedStageAndFlow;
								if (!int.TryParse(localValue, out var result11))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLinesXY(result11, out var xvals3, out var yvals3))
								{
									return BAD;
								}
								boundary.Stage = xvals3;
								boundary.Flow = yvals3;
								goto IL_0c65;
							}
							case "Precipitation Hydrograph":
							{
								boundary.HydrographType = HydrographType.Precipitation;
								if (!int.TryParse(localValue, out var result6))
								{
									pr.ReportError("Could not parse hydrograph count.");
									return BAD;
								}
								if (!parser.ReadFloatLines(result6, out var data11))
								{
									return BAD;
								}
								boundary.Flow = data11.ToArray();
								goto IL_0c65;
							}
							case "Friction Slope":
								boundary.HydrographType = HydrographType.NormalDepth;
								data = localValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
								if (data.Length != 0)
								{
									if (float.TryParse(data[0], out var result12))
									{
										boundary.FrictionSlope = result12;
										if (data.Length > 1)
										{
											boundary.SingleWsOnBCLine = Parser.ParseAsBool(data[1]);
										}
										break;
									}
									pr.ReportError("Parse error.");
									return BAD;
								}
								pr.ReportError("Parse error.");
								return BAD;
							case "Gate Name":
							{
								boundary.HydrographType = HydrographType.GateOpening;
								boundary.Gates = new List<BoundaryGate>();
								parser.UndoLine();
								string textValue9;
								while (parser.CheckKey("Gate Name", out textValue9))
								{
									BoundaryGate boundaryGate = new BoundaryGate();
									boundary.Gates.Add(boundaryGate);
									boundaryGate.Name = textValue9;
									if (parser.CheckKey("Gate DSS File", out string dssFile))
									{
										boundaryGate.DssFile = dssFile;
									}
									if (parser.CheckKey("Gate DSS Path", out string dssPath))
									{
										boundaryGate.DssPath = dssPath;
									}
									if (parser.CheckKey("Gate Use DSS", out bool useDss))
									{
										boundaryGate.UseDss = useDss;
									}
									if (parser.CheckKey("Gate Time Interval", out string timeInterval2))
									{
										boundaryGate.TimeInterval = timeInterval2;
									}
									if (parser.CheckKey("Gate Use Fixed Start Time", out bool useFixedStartTime))
									{
										boundaryGate.UseFixedStartTime = useFixedStartTime;
									}
									if (parser.CheckKey("Gate Fixed Start Date/Time", out string fixedStartDateTime))
									{
										boundaryGate.FixedStartDateTime = fixedStartDateTime;
									}
									if (parser.CheckKey("Gate Openings", out int ordinal2))
									{
										parser.ReadFloatLines(ordinal2, out var data14);
										boundaryGate.OpeningHeights = data14;
									}
								}
								break;
							}
							case "Elev Controlled Gate":
							{
								boundary.ElevControlledGates = new List<ElevControlledGate>();
								parser.UndoLine();
								string textValue10;
								while (parser.CheckKey("Elev Controlled Gate", out textValue10))
								{
									string[] line = textValue10.Split(',');
									string[] line2 = Array.Empty<string>();
									string[] line3 = Array.Empty<string>();
									if (parser.CheckKey("Elev Controlled Gate Ref", out string textValue11))
									{
										line2 = textValue11.Split(',');
									}
									if (parser.CheckKey("Elev Controlled Gate Stage Diff", out string textValue12))
									{
										line3 = textValue12.Split(',');
									}
									boundary.ElevControlledGates.Add(new ElevControlledGate(line, line2, line3));
								}
								break;
							}
							case "Navigation Dam":
							{
								NavigationDam navigationDam = new NavigationDam(localValue.Split(','));
								if (boundary.NavigationDam != null)
								{
									pr.ReportError("Multiple Navigation Dams");
									return BAD;
								}
								boundary.NavigationDam = navigationDam;
								if (parser.CheckKey("Navigation Dam SFT", out string textValue13))
								{
									if (!int.TryParse(textValue13, out var result4))
									{
										pr.ReportError("Could not parse " + textValue13.InDoubleQuotes() + " as integer");
										return BAD;
									}
									if (!parser.ReadFloatLines(result4, out var data9))
									{
										pr.ReportError("Could not read data array on navigation dam");
										return BAD;
									}
									navigationDam.Flow = data9;
									if (!parser.ReadFloatLines(result4, out data9))
									{
										pr.ReportError("Could not read data array on navigation dam");
										return BAD;
									}
									navigationDam.WSMax = data9;
									if (!parser.ReadFloatLines(result4, out data9))
									{
										pr.ReportError("Could not read data array on navigation dam");
										return BAD;
									}
									navigationDam.WSMin = data9;
									string textValue14;
									while (parser.CheckKey("Navigation Dam Flow Monitor RRR", out textValue14))
									{
										navigationDam.FlowMonitors.Add(new RRS(textValue14));
									}
									if (parser.CheckKey("Navigation Dam Hinge Point RRR", out string textValue15))
									{
										navigationDam.HingePointRRS = new RRS(textValue15);
									}
									if (!parser.AssertGetNextLine(out var l))
									{
										return BAD;
									}
									navigationDam.HingePointLine = l;
									if (!parser.AssertGetNextLine(out var l2))
									{
										return BAD;
									}
									navigationDam.MinPoolLine = l2;
									if (!parser.AssertGetNextLine(out var l3))
									{
										return BAD;
									}
									navigationDam.MaxPoolLine = l3;
								}
								break;
							}
							case "Rule Expression":
							case "Rule Gate Data":
							case "Rule Operation":
							case "Rule Table":
								pr.ReportMessage("Rules not supported yet. Skipping Ruleset starting with: " + parser.CurrentLine);
								parser.UndoLine();
								while (true)
								{
									if (parser.CheckKey("Rule Operation", out textValue7))
									{
										continue;
									}
									while (parser.CheckKey("Rule Expression", out textValue7))
									{
									}
									while (parser.CheckKey("Rule Gate Data", out textValue7))
									{
									}
									string textValue16;
									while (parser.CheckKey("Rule Table", out textValue16))
									{
										data = textValue16.Split(',');
										if (data.Length != 2)
										{
											pr.ReportError("Could not parse Row/Cols from Rule Table: " + parser.PriorLine);
											return BAD;
										}
										if (int.TryParse(data[0], out var result) && int.TryParse(data[1], out var result2))
										{
											if (!parser.ReadFloatLines(result, out var _))
											{
												pr.ReportError("Error parsing rule tables.");
												return BAD;
											}
											float[] data5;
											if (result2 > 1)
											{
												if (!parser.ReadFloatLines(result2, out var _))
												{
													pr.ReportError("Error parsing rule tables.");
													return BAD;
												}
												for (int i = 0; i < result2; i++)
												{
													if (!parser.ReadFloatLines(result, out var _))
													{
														pr.ReportError("Error parsing rule tables.");
														return BAD;
													}
												}
											}
											else if (!parser.ReadFloatLines(result, out data5))
											{
												pr.ReportError("Error parsing rule tables.");
												return BAD;
											}
											continue;
										}
										pr.ReportError("Could not parse rule table count.");
										return BAD;
									}
									switch (parser.TryPeekKey())
									{
									case "Rule Table":
									case "Rule Expression":
									case "Rule Gate Data":
									case "Rule Operation":
										continue;
									}
									break;
								}
								break;
							default:
								{
									parser.UndoLine();
									break;
								}
								IL_0c65:
								if (parser.CheckKey("Flow Hydrograph QMin", out computed))
								{
									boundary.FlowHydroQMin = computed;
								}
								if (parser.CheckKey("Flow Hydrograph QMult", out computed))
								{
									boundary.FlowHydroQMult = computed;
								}
								if (parser.CheckKey("Flow Hydrograph Slope", out computed))
								{
									boundary.FlowHydroSlope = computed;
								}
								if (parser.CheckKey("DSS File", out hydroDssFile))
								{
									boundary.HydroDssFile = hydroDssFile;
								}
								if (parser.CheckKey("DSS Path", out hydroDssPath))
								{
									boundary.HydroDssPath = hydroDssPath;
								}
								if (parser.CheckKey("DSS Path2", out hydroDssPath2))
								{
									boundary.HydroDssPath2 = hydroDssPath2;
								}
								if (parser.CheckKey("Use DSS", out hydroUseDss))
								{
									boundary.HydroUseDss = hydroUseDss;
								}
								if (parser.CheckKey("CWMS InputPosition", out condition))
								{
									pr.ReportMessage("Skipping unknown tag 'CWMS InputPosition'", LogLevel.Debug);
								}
								if (parser.CheckKey("Use Fixed Start Time", out hydroUseAbsTime))
								{
									boundary.HydroUseAbsTime = hydroUseAbsTime;
								}
								if (parser.CheckKey("Fixed Start Date/Time", out hydroFixedStartDt))
								{
									boundary.HydroFixedStartDt = hydroFixedStartDt;
								}
								if (parser.CheckKey("Is Critical Boundary", out hydroIsCritBoundary))
								{
									boundary.HydroIsCritBoundary = hydroIsCritBoundary;
								}
								if (parser.CheckKey("Critical Boundary Flow", out hydroCritBoundaryFlow))
								{
									boundary.HydroCritBoundaryFlow = hydroCritBoundaryFlow;
								}
								break;
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						string textValue17;
						while (parser.CheckKey("Met Station Name", out textValue17))
						{
							MetStation metStation = new MetStation();
							MetStations.Add(metStation);
							metStation.Name = textValue17;
							if (parser.CheckKey("Met Station Gage Height", out float gaugeHeight))
							{
								metStation.GaugeHeight = gaugeHeight;
							}
							if (parser.CheckKey("Met Station Gauge Height", out gaugeHeight))
							{
								metStation.GaugeHeight = gaugeHeight;
							}
							if (parser.CheckKey("Met Station LL", out string textValue18))
							{
								if (!Parser.ParseCSV(textValue18, out var values, skipEmpties: false))
								{
									pr.ReportError("Error parsing Met Station Lat/Lon CSV");
									return BAD;
								}
								if (values.Length != 2)
								{
									pr.ReportError("Error parsing Met Station Lat/Lon CSV");
									return BAD;
								}
								metStation.Longitude = values[0];
								metStation.Latitude = values[1];
							}
							if (parser.CheckKey("Met Station XY", out textValue18))
							{
								if (!Parser.ParseCSV(textValue18, out var values2, skipEmpties: false))
								{
									pr.ReportError("Error parsing Met Station X/Y CSV");
									return BAD;
								}
								if (values2.Length != 2)
								{
									pr.ReportError("Error parsing Met Station X/Y CSV");
									return BAD;
								}
								metStation.X = values2[0];
								metStation.Y = values2[1];
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						if (parser.CheckKey("Met Point Raster Parameters", out string textValue19))
						{
							string[] parts = textValue19.Split(',', StringSplitOptions.RemoveEmptyEntries);
							if (parts.Length == 0)
							{
								MetRasterDefinition = RasterDefinition.NullRaster;
							}
							else if (parts.Length != 5)
							{
								pr.ReportMessage("Met Raster Definition came in as " + textValue19.InDoubleQuotes() + " - setting to null.", LogLevel.Debug);
								MetRasterDefinition = RasterDefinition.NullRaster;
							}
							else
							{
								if (!double.TryParse(parts[0], out var result14) || !double.TryParse(parts[1], out var result15) || !int.TryParse(parts[2], out var result16) || !int.TryParse(parts[3], out var result17) || !double.TryParse(parts[4], out var result18))
								{
									pr.ReportError("Bad met raster definition parsing");
									return BAD;
								}
								MetRasterDefinition = new RasterDefinition(new Point(result14, result15), result18, result16, result17);
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						if (parser.CheckKey("Precipitation Mode", out string precipitationMode) && !string.IsNullOrEmpty(precipitationMode))
						{
							PrecipitationMode = precipitationMode;
						}
						if (parser.CheckKey("Wind Mode", out string windMode) && !string.IsNullOrEmpty(windMode))
						{
							WindMode = windMode;
						}
						if (parser.CheckKey("Air Density Mode", out string airDensityMode) && !string.IsNullOrEmpty(airDensityMode))
						{
							AirDensityMode = airDensityMode;
						}
						bool condition2 = false;
						MetBC metBC = null;
						string textValue20;
						string key2;
						string textValue21;
						while (parser.CheckCompoundKey("Met BC", out textValue20, out key2, out textValue21))
						{
							if (metBC == null || metBC.Name != textValue20)
							{
								metBC = new MetBC();
								metBC.Name = textValue20;
								MetBCs.Add(metBC);
							}
							switch (key2)
							{
							case "Expanded View":
								break;
							case "Mode":
								metBC.Mode = textValue21;
								break;
							case "Constant Value":
							{
								if (float.TryParse(textValue21, out var result20))
								{
									metBC.ConstValue = result20;
									break;
								}
								pr.ReportError("Could not parse " + textValue21.InDoubleQuotes() + " as float. (Met BC data)");
								return BAD;
							}
							case "Constant Units":
								metBC.ConstUnits = textValue21;
								break;
							case "Point Interpolation":
								metBC.PointInterpolation = textValue21;
								break;
							case "Point Name":
							case "TS Name":
							{
								MetPointGauge metPointGauge = new MetPointGauge();
								metPointGauge.Name = textValue21;
								metBC.PointGauges.Add(metPointGauge);
								break;
							}
							case "Point Used":
							case "TS Used":
								metBC.PointGauges.Last().Used = Parser.ParseAsBool(textValue21);
								break;
							case "Point Source":
							case "TS Source":
								metBC.PointGauges.Last().Source = textValue21;
								break;
							case "Point Constant Units":
							case "TS Constant Units":
								metBC.PointGauges.Last().ConstantUnits = textValue21;
								break;
							case "Point DSS Filename":
							case "TS DSS Filename":
								metBC.PointGauges.Last().DssFile = textValue21;
								break;
							case "Point DSS Pathname":
							case "TS DSS Pathname":
								metBC.PointGauges.Last().DssPath = textValue21;
								break;
							case "Point Table Mode":
							case "TS Table Mode":
								metBC.PointGauges.Last().TableMode = textValue21;
								break;
							case "Point Table Use Fixed Start":
							case "TS Table Use Fixed Start":
								metBC.PointGauges.Last().TableUseFixedStart = Parser.ParseAsBool(textValue21);
								break;
							case "Point Table Use Plan Start":
							case "TS Table Use Plan Start":
								metBC.PointGauges.Last().TableUseFixedStart = !Parser.ParseAsBool(textValue21);
								break;
							case "Point Table StartDateTime":
							case "TS Table StartDateTime":
								metBC.PointGauges.Last().TableStartDate = textValue21;
								break;
							case "Point Table Interval":
							case "TS Table Interval":
								metBC.PointGauges.Last().TableInterval = textValue21;
								break;
							case "Point Table Data Type":
							case "TS Table Data Type":
								metBC.PointGauges.Last().TableDataType = textValue21;
								break;
							case "Point Table Data":
							case "TS Table Data":
							{
								if (int.TryParse(textValue21, out var result19))
								{
									if (parser.ReadDoubleLinesXY(result19, out var xvals4, out var yvals4))
									{
										metBC.PointGauges.Last().TableXData = xvals4;
										metBC.PointGauges.Last().TableYData = yvals4;
										break;
									}
									pr.ReportError("Could not parse Point Table Data for Met BC " + metBC.Name);
									return BAD;
								}
								pr.ReportError("Could not parse Point Table Data count for Met BC " + metBC.Name);
								return BAD;
							}
							case "Point Table Units":
							case "TS Table Units":
								metBC.PointGauges.Last().TableUnits = textValue21;
								break;
							case "Point Table Data Units":
							case "TS Table Data Units":
								metBC.PointGauges.Last().TableUnits = textValue21;
								break;
							case "Gridded Source":
								metBC.GriddedSource = textValue21;
								break;
							case "Gridded DSS Filename":
								metBC.GriddedDssFile = textValue21;
								break;
							case "Gridded DSS Pathname":
								metBC.GriddedDssPath = textValue21;
								break;
							case "Gridded GDAL Filename":
								metBC.GriddedGdalFile = textValue21;
								break;
							case "Gridded GDAL Group":
								metBC.GriddedGdalGroup = textValue21;
								break;
							case "Gridded GDAL Folder":
								metBC.GriddedGdalFolder = textValue21;
								break;
							case "Gridded GDAL Filter":
								metBC.GriddedGdalFilter = textValue21;
								break;
							default:
								pr.ReportError("Unknown Met BC Arg: " + key2);
								return BAD;
							}
						}
						while (parser.CheckKey("Met Wind", out textValue7))
						{
						}
						while (parser.CheckKey("Met Precip", out textValue7))
						{
						}
						condition2 = false;
						if (parser.CheckKey("Water Temperature Hydrograph", out string textValue22))
						{
							if (!int.TryParse(textValue22, out var result21))
							{
								pr.ReportError("Could not parse Water Temp Hydrograph count.");
								return BAD;
							}
							if (!parser.ReadFloatLines(result21, out var _))
							{
								pr.ReportError("Could not parse Water Temp Hydrograph vals.");
								return BAD;
							}
							parser.CheckKey("Water Temperature Interval", out string _);
							parser.CheckKey("Water Temperature Hydrogrpah QMin", out float _);
							parser.CheckKey("Water Temperature Hydrogrpah QMult", out float _);
							parser.CheckKey("Water Temperature DSS File", out string _);
							parser.CheckKey("Water Temperature DSS Path", out string _);
							parser.CheckKey("Water Temperature Use DSS", out bool _);
							parser.CheckKey("Water Temperature Use Fixed Start Time", out bool _);
							parser.CheckKey("Water Temperature Fixed Start Date/Time", out string _);
							if (!condition2)
							{
								pr.ReportMessage("Water Temperature not currently supported. Skipping e.g. " + parser.CurrentLine);
								condition2 = true;
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						condition2 = false;
						string textValue23;
						while (parser.CheckKey("Observed DSS Loc", out textValue23))
						{
							parser.CheckKey("Observed DSS Filename", out string _);
							parser.CheckKey("Observed DSS Pathname", out string _);
							parser.CheckKey("Observed DSS Dn Dist", out float _);
							if (!condition2)
							{
								pr.ReportMessage("Observed DSS not currently supported. Skipping e.g. " + parser.CurrentLine);
								condition2 = true;
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						string textValue24;
						while (parser.CheckKey("Observed HWM", out textValue24))
						{
							string[] partsArray = textValue24.Split(',');
							if (partsArray.Length != 5)
							{
								pr.ReportError("Error parsing Observed HWM: " + textValue24);
								return BAD;
							}
							RRS_HWM rRS_HWM = new RRS_HWM
							{
								RRS = new RRS(partsArray)
							};
							if (string.IsNullOrEmpty(partsArray[3]))
							{
								rRS_HWM.DownstreamDistance = float.NaN;
							}
							else
							{
								if (!float.TryParse(partsArray[3], out var result22))
								{
									pr.ReportError("Error parsing Observed HWM Downstream Distance");
									return BAD;
								}
								rRS_HWM.DownstreamDistance = result22;
							}
							if (string.IsNullOrEmpty(partsArray[4]))
							{
								rRS_HWM.Elevation = float.NaN;
								continue;
							}
							if (float.TryParse(partsArray[4], out var result23))
							{
								rRS_HWM.Elevation = result23;
								continue;
							}
							pr.ReportError("Error parsing Observed HWM Elevation");
							return BAD;
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						condition2 = false;
						string textValue25;
						while (parser.CheckKey("Observed RC", out textValue25))
						{
							if (!condition2)
							{
								pr.ReportMessage("Observed RC not currently supported. Skipping e.g. " + parser.CurrentLine);
								condition2 = true;
							}
							parser.CheckKey("Observed RC Desc", out string _);
							if (parser.CheckKey("Observed RC Data", out int ordinal3) && !parser.ReadFloatLinesXY(ordinal3, out var _, out var _))
							{
								pr.ReportError("Expected observed RC data lines.");
								return BAD;
							}
							if (!parser.CheckKey("Observed RC Point Data", out int count))
							{
								continue;
							}
							for (int j = 0; j < count; j++)
							{
								if (!parser.AssertGetNextLine(out var l4))
								{
									return BAD;
								}
								if (l4.Length < 32)
								{
									pr.ReportError("Expected 2 doubles @ 16 chars each: " + l4.InDoubleQuotes());
									return BAD;
								}
								if (!double.TryParse(l4.AsSpan(0, 16), out var _))
								{
									pr.ReportError("Observed RC Point Data parsing error.");
									return BAD;
								}
								if (!double.TryParse(l4.AsSpan(16, 16), out var _))
								{
									pr.ReportError("Observed RC Point Data parsing error.");
									return BAD;
								}
								if (l4.Length > 32)
								{
									l4.Substring(32);
								}
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						condition2 = false;
						string textValue26;
						while (parser.CheckKey("Observed RRR Location", out textValue26))
						{
							if (!condition2)
							{
								pr.ReportMessage("Observed RRR Location not currently supported. Skipping e.g. " + parser.CurrentLine);
								condition2 = true;
							}
							if (!parser.CheckKey("Observed RRR DS Distance", out float _))
							{
							}
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
						condition2 = false;
						string textValue27;
						while (parser.CheckKey("Observed Time Series", out textValue27) && textValue27.StartsWith("Stage"))
						{
							if (!condition2)
							{
								pr.ReportMessage("Obs TS Stage not currently supported: " + parser.CurrentLine);
								condition2 = true;
							}
						}
						condition2 = false;
						string textValue28;
						while (parser.CheckKey("Observed Time Series", out textValue28) && textValue28.StartsWith("Flow"))
						{
							if (!condition2)
							{
								pr.ReportMessage("Obs TS Flow not currently supported: " + parser.CurrentLine);
								condition2 = true;
							}
						}
						condition2 = false;
						string textValue29;
						while (parser.CheckKey("Observed Rating Curve", out textValue29))
						{
							if (!condition2)
							{
								pr.ReportMessage("Obs RC not currently supported: " + parser.CurrentLine);
								condition2 = true;
							}
							if (Parser.SplitKVP(textValue29, out var key3, out var localValue2) && key3 == "Table Data")
							{
								if (!int.TryParse(localValue2, out var result26))
								{
									pr.ReportError("Could not parse Table Data count " + localValue2.InDoubleQuotes() + " as integer.");
									return BAD;
								}
								if (!parser.ReadDoubleLinesXY(result26, out var _, out var _))
								{
									pr.ReportError("Could not read Observed Rating Curve table data.");
									return BAD;
								}
							}
						}
						condition2 = false;
						string textValue30;
						while (parser.CheckKey("Observed High Water Mark", out textValue30))
						{
							if (!condition2)
							{
								pr.ReportMessage("Obs HWM not currently supported: " + parser.CurrentLine);
								condition2 = true;
							}
						}
						if (parser.CheckKey("Non-Newtonian Method", out string textValue31))
						{
							NonNewtonian.Method = textValue31;
						}
						if (parser.CheckKey("Non-Newtonian Constant Vol Conc", out string constVolumeConcentration))
						{
							NonNewtonian.ConstVolumeConcentration = constVolumeConcentration;
						}
						if (parser.CheckKey("Non-Newtonian Yield Method", out string yieldMethod))
						{
							NonNewtonian.YieldMethod = yieldMethod;
						}
						if (parser.CheckKey("Non-Newtonian Yield Coef", out string yieldCoefficient))
						{
							NonNewtonian.YieldCoefficient = yieldCoefficient;
						}
						if (parser.CheckKey("User Yeild", out string userYield))
						{
							NonNewtonian.UserYield = userYield;
						}
						if (parser.CheckKey("Non-Newtonian Sed Visc", out string sedViscosity))
						{
							NonNewtonian.SedViscosity = sedViscosity;
						}
						if (parser.CheckKey("Non-Newtonian Obrian B", out string obrianB))
						{
							NonNewtonian.ObrianB = obrianB;
						}
						if (parser.CheckKey("User Viscosity", out string userViscosity))
						{
							NonNewtonian.UserViscosity = userViscosity;
						}
						if (parser.CheckKey("User Viscosity Ratio", out string userViscosityRatio))
						{
							NonNewtonian.UserViscosityRatio = userViscosityRatio;
						}
						if (parser.CheckKey("Herschel-Bulkley Coef", out string herschelCoef))
						{
							NonNewtonian.HerschelCoef = herschelCoef;
						}
						if (parser.CheckKey("Clastic Method", out string clasicMethod))
						{
							NonNewtonian.ClasicMethod = clasicMethod;
						}
						if (parser.CheckKey("Voellmy Phi", out string coulombPhi))
						{
							NonNewtonian.CoulombPhi = coulombPhi;
						}
						if (parser.CheckKey("Coulomb Phi", out string coulombPhi2))
						{
							NonNewtonian.CoulombPhi = coulombPhi2;
						}
						if (parser.CheckKey("Voellmy X", out string voellmyX))
						{
							NonNewtonian.VoellmyX = voellmyX;
						}
						if (parser.CheckKey("Non-Newtonian Hindered FV", out string hinderedFV))
						{
							NonNewtonian.HinderedFV = hinderedFV;
						}
						if (parser.CheckKey("Non-Newtonian FV K", out string fvk))
						{
							NonNewtonian.FVK = fvk;
						}
						if (parser.CheckKey("Non-Newtonian ds", out string ds))
						{
							NonNewtonian.DS = ds;
						}
						if (parser.CheckKey("Non-Newtonian Max Cv", out string maxCV))
						{
							NonNewtonian.MaxCV = maxCV;
						}
						if (parser.CheckKey("Non-Newtonian Bulking Method", out string bulkingMethod))
						{
							NonNewtonian.BulkingMethod = bulkingMethod;
						}
						if (parser.CheckKey("Non-Newtonian High C Transport", out string highCTransport))
						{
							NonNewtonian.HighCTransport = highCTransport;
						}
						while (parser.TryPeekKey().Contains("Newotonian"))
						{
							parser.LineIndex++;
						}
						if (parser.EOF)
						{
							return FileState.Good;
						}
					}
					while (parser.LineIndex != lineIndex);
					pr.ReportError("Could not resolve line [" + lineIndex + "]: " + parser.CurrentLine);
					return BAD;
				}
				return BAD;
			}
			return BAD;
		}
		return BAD;
	}

	private FileState ParseInternalSteady(string fn, ProgressReporter pr)
	{
		pr.ReportError("Steady flow files not supported yet.");
		return BAD;
	}

	private static bool ParseAsNamedValue(string textValue, ProgressReporter pr, out NamedValue nv)
	{
		string[] parts = textValue.Split(',', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2)
		{
			pr.ReportError("Could not parse CSV " + textValue);
			nv = default;
			return false;
		}
		string n = parts[0].Trim();
		string trim = parts[1].Trim();
		if (float.TryParse(trim, out var result))
		{
			nv = new NamedValue(n, result);
			return true;
		}
		pr.ReportError("Could not parse float " + trim);
		nv = default;
		return false;
	}

	private static bool ParseAsRRSFloatPair(string textValue, ProgressReporter pr, out RRSValue rv)
	{
		string[] parts = textValue.Split(',');
		if (parts.Length != 4)
		{
			pr.ReportError("Could not parse Riv/Rch/Rs/Value CSV " + textValue.InSingleQuotes());
			rv = default;
			return false;
		}
		string riv = parts[0].Trim();
		string rch = parts[1].Trim();
		string rs = parts[2].Trim();
		string trim = parts[3].Trim();
		if (float.TryParse(trim, out var result))
		{
			rv = new RRSValue(riv, rch, rs, result);
			return true;
		}
		pr.ReportError("Could not parse float " + trim);
		rv = default;
		return false;
	}

	public void Export7thGeneration(string exportDirectory, ProgressReporter reporter)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (FileStatus == FileState.DoesNotExist)
		{
			reporter.ReportError("Could not migrate " + Filename.InDoubleQuotes() + " (does not exist)");
			return;
		}
		string sanitizeFilename = FileHelpers.SanitizeFilename(Name);
		Ras.Layers.BoundaryCondition boundaryCondition = Ras.Layers.BoundaryCondition.CreateNew(Path.Combine(Ras.Layers.Project.GetDefaultBoundaryConditionDirectory(exportDirectory), sanitizeFilename + ".h5"));
		boundaryCondition.Save();
		stopwatch.Stop();
		reporter.ReportMessage("Converted " + Path.GetFileName(Filename) + " " + Name.InParentheses() + " to " + Path.GetFileName(boundaryCondition.Filename).InDoubleQuotes() + " in " + stopwatch.ElapsedMilliseconds + " ms.");
	}

	public override string ToString()
	{
		return Name;
	}
}
