using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Ras.Layers;
using Utility.Extensions;
using Utility.Files;
using Utility.IO;
using Utility.Progress;

namespace Ras.Migration.Native;

public class Plan
{
	public Project Project { get; set; }

	public string Version { get; set; }

	public string Filename { get; set; }

	public string ResultHDF5Filename => Filename + ".hdf";

	public string Name { get; set; }

	public string ShortID { get; set; }

	public string Description { get; set; }

	public string AssociatedGeometryExtension { get; set; }

	public string AssociatedFlowExtension { get; set; }

	public string AssociatedSedimentExtension { get; set; }

	public string AssociatedWqExtension { get; set; }

	public string AssociatedDssFile { get; set; }

	public DateTime SimulationStart { get; set; }

	public DateTime SimulationEnd { get; set; }

	public PlanFlowType FlowType { get; set; }

	public string SumByGR { get; set; }

	public JobControlInformation SteadyTolerance { get; set; } = new JobControlInformation();

	public string LogOutputLevel { get; set; }

	public string FrictionSlopeMethod { get; set; }

	public bool CriticalByParabolic { get; set; }

	public VelocityDist GlobalVelocityDistr { get; set; }

	public string UnsteadyFrictionSlopeMethod { get; set; }

	public string UnsteadyBridgesFrictionSlopeMethod { get; set; }

	public Dictionary<RRS, VelocityDist> CrossSectionVelocityDistr { get; set; } = new Dictionary<RRS, VelocityDist>();

	public bool CheckData { get; set; }

	public string Extension => Path.GetExtension(Filename)?.Substring(1);

	public Geometry Geometry { get; set; }

	public BoundaryCondition BoundaryConditions { get; set; }

	public PlanType Type { get; set; }

	public FileState FileStatus { get; private set; }

	public string ComputationInterval { get; set; }

	public string OutputInterval { get; set; }

	public string InstantaneousInterval { get; set; }

	public string MappingInterval { get; set; }

	public AdvancedTimestepControl TimestepControl { get; set; } = new AdvancedTimestepControl();

	public ComputationLevelOutput ComputationLevelOutput { get; set; } = new ComputationLevelOutput();

	public HDFWriteParams WriteParams { get; set; } = new HDFWriteParams();

	public List<RRS> StageFlowLocations { get; set; } = new List<RRS>();

	public SedimentTolerance SedimentTolerance { get; set; } = new SedimentTolerance();

	public Dictionary<string, string> PlanData { get; }

	public static FileState BAD => FileState.Malformed;

	private Plan()
	{
	}

	public Plan(Project prj, string planFilename)
	{
		if (prj == null)
		{
			throw new ArgumentNullException("prj");
		}
		Project = prj;
		Filename = planFilename;
		if (!System.IO.File.Exists(planFilename))
		{
			FileStatus = FileState.DoesNotExist;
			Type = PlanType.Unknown;
			return;
		}
		Utility.IO.File.ParseKVPDelimitedFile(planFilename, '=', out Dictionary<string, string> kvp);
		PlanData = kvp;
		try
		{
			Name = PlanData["Plan Title"];
			ShortID = PlanData["Short Identifier"];
			if (!PlanData.TryGetValue("Flow File", out var associatedFlowExtension))
			{
				Type = PlanType.Unknown;
			}
			else
			{
				AssociatedFlowExtension = associatedFlowExtension;
				if (associatedFlowExtension[0] == 'f')
				{
					Type = PlanType.Steady;
				}
				else if (associatedFlowExtension[0] == 'u')
				{
					Type = PlanType.Unsteady;
				}
				else if (associatedFlowExtension[0] == 'q')
				{
					Type = PlanType.QuasiUnsteady;
				}
				else
				{
					Type = PlanType.Unknown;
				}
				BoundaryConditions = prj.BoundaryConditions.FirstOrDefault((BoundaryCondition ev) => ev.FlowExtension.RoughlyEquals(AssociatedFlowExtension));
			}
			if (PlanData.TryGetValue("Geom File", out var associatedGeometryExtension))
			{
				AssociatedGeometryExtension = associatedGeometryExtension;
				Geometry = prj.Geometries.FirstOrDefault((Geometry geom) => geom.Extension.RoughlyEquals(AssociatedGeometryExtension));
			}
		}
		catch
		{
			FileStatus = FileState.Malformed;
		}
	}

	public void Export7thGeneration(string exportDirectory, ProgressReporter reporter)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (Project == null)
		{
			reporter.ReportError("No associated project for plan " + Name.InDoubleQuotes());
			return;
		}
		if (FileStatus == FileState.DoesNotExist)
		{
			reporter.ReportError("Could not migrate " + Filename.InDoubleQuotes() + " (does not exist)");
			return;
		}
		EnsurePlanDirectory(exportDirectory);
		FileHelpers.SanitizeFilename(Name);
		stopwatch.Stop();
	}

	private static string EnsurePlanDirectory(string exportDirectory)
	{
		string defaultPlanDirectory = Ras.Layers.Project.GetDefaultPlanDirectory(exportDirectory);
		if (!Directory.Exists(defaultPlanDirectory))
		{
			Directory.CreateDirectory(defaultPlanDirectory);
		}
		return defaultPlanDirectory;
	}

	public static bool TryParse(string filename, out Plan pl, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		pl = new Plan();
		pl.Filename = filename;
		try
		{
			if (!System.IO.File.Exists(pl.Filename))
			{
				pl.FileStatus = FileState.DoesNotExist;
				return false;
			}
			pl.FileStatus = pl.ParseInternal(pr);
			return pl.FileStatus == FileState.Good;
		}
		catch
		{
			pl.FileStatus = FileState.Malformed;
			return false;
		}
	}

	private FileState ParseInternal(ProgressReporter pr)
	{
		Parser parser = new Parser(System.IO.File.ReadAllLines(Filename), pr);
		if (!parser.AssertKey("Plan Title", (string pt) =>
		{
			Name = pt;
		}))
		{
			return BAD;
		}
		parser.CheckKey("Program Version", (string ver) =>
		{
			Version = ver;
		});
		parser.CheckKey("Version", (string ver) =>
		{
			Version = ver;
		});
		parser.CheckKey("Short Identifier", (string sid) =>
		{
			ShortID = sid;
		});
		if (parser.CheckKey("Simulation Date", out string textValue))
		{
			if (!TryParseDateRange(textValue, out var dtStart, out var dtEnd))
			{
				return BAD;
			}
			SimulationStart = dtStart;
			SimulationEnd = dtEnd;
		}
		else
		{
			SimulationStart = DateTime.MinValue;
			SimulationEnd = DateTime.MinValue;
		}
		parser.CheckKey("Geom File", (string gf) =>
		{
			AssociatedGeometryExtension = gf;
		});
		parser.CheckKey("Flow File", (string ff) =>
		{
			AssociatedFlowExtension = ff;
		});
		parser.CheckKey("Sediment File", (string sf) =>
		{
			AssociatedSedimentExtension = sf;
		});
		parser.CheckKey("Water Quality File", (string wqf) =>
		{
			AssociatedWqExtension = wqf;
		});
		parser.CheckTag("Interactive Comp Mode");
		parser.CheckTag("Multiple Comp Mode");
		if (!parser.AssertGetNextLine(out var l))
		{
			return BAD;
		}
		if (l.RoughlyEquals("Subcritical Flow"))
		{
			FlowType = PlanFlowType.Subcritical;
		}
		else if (l.RoughlyEquals("Supercritical Flow"))
		{
			FlowType = PlanFlowType.Supercritical;
		}
		else
		{
			if (!l.RoughlyEquals("Mixed Flow"))
			{
				pr.ReportError("Could not parse flow type: " + l);
				return BAD;
			}
			FlowType = PlanFlowType.Mixed;
		}
		int lineIndex;
		do
		{
			lineIndex = parser.LineIndex;
			parser.CheckKey("Starting Rch & RM", out string textValue2);
			parser.CheckKey("Ending Rch & RM", out textValue2);
			parser.CheckKey("K Sum by GR", (string v) =>
			{
				SumByGR = v;
			});
			parser.CheckKey("Std Step Tol", (float v) =>
			{
				SteadyTolerance.StandardStepTol = v;
			});
			parser.CheckKey("Critical Tol", (float v) =>
			{
				SteadyTolerance.CriticalTol = v;
			});
			parser.CheckKey("Num of Std Step Trials", (int v) =>
			{
				SteadyTolerance.NumStdStepTrials = v;
			});
			parser.CheckKey("Max Error Tol", (float v) =>
			{
				SteadyTolerance.MaxErrorTol = v;
			});
			parser.CheckKey("Flow Tol Ratio", (float v) =>
			{
				SteadyTolerance.FlowTolRatio = v;
			});
			parser.CheckKey("Split Flow NTrial", (int v) =>
			{
				SteadyTolerance.NumSplitFlowTrials = v;
			});
			parser.CheckKey("Split Flow Tol", (float v) =>
			{
				SteadyTolerance.SplitFlowTol = v;
			});
			parser.CheckKey("Split Flow Ratio", (float v) =>
			{
				SteadyTolerance.SplitFlowRatio = v;
			});
			parser.CheckKey("Log Output Level", (string s) =>
			{
				LogOutputLevel = s;
			});
			parser.CheckKey("Friction Slope Method", (string s) =>
			{
				FrictionSlopeMethod = s;
			});
			parser.CheckKey("Unsteady Friction Slope Method", (string s) =>
			{
				UnsteadyFrictionSlopeMethod = s;
			});
			parser.CheckKey("Unsteady Bridges Friction Slope Method", (string s) =>
			{
				UnsteadyBridgesFrictionSlopeMethod = s;
			});
			if (parser.CheckTag("Calc Critical at every XS"))
			{
				SteadyTolerance.AlwaysCompCrit = true;
			}
			if (parser.CheckTag("Parabolic Critical Depth"))
			{
				CriticalByParabolic = true;
			}
			if (parser.CheckKey("Global Vel Dist", out string textValue3))
			{
				if (!Parser.ParseCSV(textValue3, out int[] parsed) || parsed.Length != 3)
				{
					pr.ReportError("Unexpected Global Velocity Distribution: " + textValue3.InDoubleQuotes());
					return BAD;
				}
				GlobalVelocityDistr = new VelocityDist
				{
					Left = parsed[0],
					Channel = parsed[1],
					Right = parsed[2]
				};
			}
			string textValue4;
			while (parser.CheckKey("Vel Dist Location", out textValue4))
			{
				string[] parts = textValue4.Split(',');
				if (parts.Length != 6)
				{
					pr.ReportError("Unexpected Local Velocity Distribution: " + textValue4.InDoubleQuotes());
					return BAD;
				}
				RRS key = new RRS(parts[0], parts[1], parts[2]);
				VelocityDist crossSectionVelocityDistr = new VelocityDist
				{
					Left = int.Parse(parts[3]),
					Channel = int.Parse(parts[4]),
					Right = int.Parse(parts[5])
				};
				CrossSectionVelocityDistr[key] = crossSectionVelocityDistr;
			}
			parser.CheckKey("Global Log Level", out textValue2);
			while (parser.CheckKey("Log Level Location", out textValue2))
			{
			}
			parser.CheckKey("CheckData", (bool cd) =>
			{
				CheckData = cd;
			});
			parser.CheckKey("Persistent XS Scale", out textValue2);
			parser.CheckKey("Persistent RC Scale", out textValue2);
			if (parser.CheckKey("Encroach Param", out string textValue5))
			{
				if (parser.CheckKey("nEncroach", out string textValue6))
				{
					pr.ReportMessage("Skipping older encroachment data encoding.");
					string[] partsArray = textValue6.Split(',');
					if (partsArray.Length != 2)
					{
						pr.ReportError("Bad (old) encroachment text: " + textValue6);
						return BAD;
					}
					int parse = int.Parse(partsArray[0]);
					int.Parse(partsArray[1]);
					for (int ordinal = 0; ordinal < parse; ordinal++)
					{
						parser.AssertGetNextLine(out textValue2);
						parser.AssertGetNextLine(out textValue2);
					}
				}
				else
				{
					if (!int.TryParse(textValue5.Split(',').Last(), out var result))
					{
						pr.ReportError("Unexpected value for Encroach Param - could not parse nEncrPf from " + textValue5.InDoubleQuotes());
						return BAD;
					}
					if (result > 0)
					{
						while (parser.CheckKey("Encroach River", out textValue2))
						{
							while (parser.CheckKey("Encroach Reach", out textValue2))
							{
								while (parser.CheckKey("Encroach Node", out textValue2))
								{
									parser.AssertGetNextLine(out textValue2);
								}
							}
						}
					}
				}
			}
			if (parser.EOF)
			{
				return FileState.Good;
			}
			while (parser.CheckKey("Unsteady Encroach River", out textValue2))
			{
				while (parser.CheckKey("Unsteady Encroach Reach", out textValue2))
				{
					while (parser.CheckKey("Unsteady Encroach RS", out textValue2))
					{
					}
				}
			}
			while (parser.CheckKey("Optimized Junction", out textValue2))
			{
			}
			while (parser.CheckKey("Optimized Storage Area", out textValue2))
			{
			}
			while (parser.CheckKey("Optimized Weir", out textValue2))
			{
			}
			while (parser.CheckKey("Optimized Pump", out textValue2))
			{
			}
			if (parser.OptionalGuardedMultiline("BEGIN DESCRIPTION:", "END DESCRIPTION:", out var description))
			{
				Description = description;
				parser.CheckKey("Computation Interval", (string ci) =>
				{
					ComputationInterval = ci;
				});
				parser.CheckKey("Output Interval", (string oi) =>
				{
					OutputInterval = oi;
				});
				parser.CheckKey("Write Instantaneous", out textValue2);
				parser.CheckKey("Instantaneous Interval", (string ii) =>
				{
					InstantaneousInterval = ii;
				});
				parser.CheckKey("Mapping Interval", (string mi) =>
				{
					MappingInterval = mi;
				});
				parser.CheckKey("Computation Time Step Use Courant", (bool b) =>
				{
					TimestepControl.UseCourant = b;
				});
				parser.CheckKey("Computation Time Step Use Time Series", (bool b) =>
				{
					TimestepControl.UseTimeSeries = b;
				});
				parser.CheckKey("Computation Time Step Max Courant", (float b) =>
				{
					TimestepControl.MaxCourant = b;
				});
				parser.CheckKey("Computation Time Step Min Courant", (float b) =>
				{
					TimestepControl.MinCourant = b;
				});
				parser.CheckKey("Computation Time Step Count To Double", (int b) =>
				{
					TimestepControl.CountToDouble = b;
				});
				parser.CheckKey("Computation Time Step Max Doubling", (int b) =>
				{
					TimestepControl.MaxDoubleCount = b;
				});
				parser.CheckKey("Computation Time Step Max Halving", (int b) =>
				{
					TimestepControl.MaxHalveCount = b;
				});
				parser.CheckKey("Computation Time Step Residence Courant", (bool b) =>
				{
					TimestepControl.UseResidenceTime = b;
				});
				while (parser.CheckKey("Computation Time Step TS", (string textValue7) =>
				{
					TimestepControl.ComputationTimestepTs.Add(textValue7);
				}))
				{
				}
				parser.CheckKey("Run HTab", out textValue2);
				parser.CheckKey("Run UNet", out textValue2);
				parser.CheckKey("Run Sediment", out textValue2);
				parser.CheckKey("Run PostProcess", out textValue2);
				parser.CheckKey("Run WQNet", out textValue2);
				parser.CheckKey("Run RASMapper", out textValue2);
				parser.SkipLines((string s) => s.StartsWith("UNET"));
				if (parser.EOF)
				{
					return FileState.Good;
				}
				parser.CheckKey("DSS File", (string dssf) =>
				{
					AssociatedDssFile = dssf;
				});
				parser.CheckKey("Write IC File", out textValue2);
				parser.CheckKey("Write IC File at Fixed DateTime", out textValue2);
				parser.CheckKey("IC Time", out textValue2);
				parser.CheckKey("Write IC File Reoccurance", out textValue2);
				parser.CheckKey("Write IC File at Sim End", out textValue2);
				parser.CheckKey("Echo Input", out textValue2);
				parser.CheckKey("Echo Parameters", out textValue2);
				parser.CheckKey("Echo Output", out textValue2);
				parser.CheckKey("Write Velocity", out textValue2);
				parser.CheckKey("Write Detailed", out textValue2);
				parser.CheckKey("Detailed Automatic Start", out textValue2);
				parser.CheckKey("HTab Param Time", out textValue2);
				parser.CheckKey("Detailed Time", out textValue2);
				if (parser.EOF)
				{
					return FileState.Good;
				}
				parser.CheckKey("Computation Level Output", (string v) =>
				{
					ComputationLevelOutput.ComputationLevel = v;
				});
				parser.CheckKey("Computation Level Output StartDate", (string v) =>
				{
					ComputationLevelOutput.StartDate = v;
				});
				parser.CheckKey("Computation Level Output StartTime", (string v) =>
				{
					ComputationLevelOutput.StartTime = v;
				});
				parser.CheckKey("Computation Level Output EndDate", (string v) =>
				{
					ComputationLevelOutput.EndDate = v;
				});
				parser.CheckKey("Computation Level Output EndTime", (string v) =>
				{
					ComputationLevelOutput.EndTime = v;
				});
				parser.CheckKey("Computation Level Output WSErr", (string v) =>
				{
					ComputationLevelOutput.WsError = v;
				});
				parser.CheckKey("Computation Level Output FlowErr", (string v) =>
				{
					ComputationLevelOutput.FlowError = v;
				});
				parser.CheckKey("Computation Level Output Depth", (string v) =>
				{
					ComputationLevelOutput.Depth = v;
				});
				parser.CheckKey("Computation Level Output Invert", (string v) =>
				{
					ComputationLevelOutput.Invert = v;
				});
				parser.CheckKey("Computation Level Output Vel Ch", (bool v) =>
				{
					ComputationLevelOutput.VelocityChannel = v;
				});
				parser.CheckKey("Computation Level Output Vel Total", (bool v) =>
				{
					ComputationLevelOutput.VelocityTotal = v;
				});
				parser.CheckKey("Computation Level Output Courant Ch", (bool v) =>
				{
					ComputationLevelOutput.CourantChannel = v;
				});
				parser.CheckKey("Computation Level Output Courant Total", (bool v) =>
				{
					ComputationLevelOutput.CourantTotal = v;
				});
				parser.CheckKey("Computation Level Output Diff Eq Parts", (string v) =>
				{
					ComputationLevelOutput.DiffEqParts = v;
				});
				if (parser.EOF)
				{
					return FileState.Good;
				}
				parser.CheckKey("HDF Write Warmup", (bool v) =>
				{
					WriteParams.WriteWarmup = v;
				});
				parser.CheckKey("HDF Write Time Slices", (bool v) =>
				{
					WriteParams.WriteTimeSlices = v;
				});
				parser.CheckKey("HDF Flush", (bool v) =>
				{
					WriteParams.Flush = v;
				});
				while (parser.CheckKey("HDF Additional Output Variable", (string v) =>
				{
					WriteParams.AdditionalOutputVars.Add(v);
				}))
				{
				}
				parser.SkipLines((string s) => s.StartsWith("HDF") && s != "HDF Compression");
				parser.CheckKey("HDF Compression", (string v) =>
				{
					WriteParams.Compression = v;
				});
				parser.CheckKey("HDF Chunk Size", (string v) =>
				{
					WriteParams.ChunkSize = v;
				});
				parser.CheckKey("HDF Spatial Parts", (string v) =>
				{
					WriteParams.SpatialParts = v;
				});
				parser.CheckKey("HDF Use Max Rows", (bool v) =>
				{
					WriteParams.UseMaxRows = v;
				});
				parser.CheckKey("HDF Fixed Rows", (string v) =>
				{
					WriteParams.FixedRows = v;
				});
				if (parser.EOF)
				{
					return FileState.Good;
				}
				while (parser.CheckKey("Stage Flow Hydrograph", (string rrs) =>
				{
					StageFlowLocations.Add(new RRS(rrs));
				}))
				{
				}
				string textValue8;
				while (parser.CheckKey("Roughness Change", out textValue8))
				{
					int expectedValueCt = int.Parse(textValue8.Split(',').Last());
					if (!parser.ReadFloatLinesXY(expectedValueCt, out var _, out var _))
					{
						pr.ReportError("Bad float data for Roughness");
						return BAD;
					}
				}
				string textValue9;
				while (parser.CheckKey("Seasonal Roughness Change", out textValue9))
				{
					string[] parts2 = textValue9.Split(',');
					if (parts2.Length != 5)
					{
						pr.ReportError("Unexpected format for Seasonal Roughness Change: " + textValue9);
						return BAD;
					}
					int parse2 = int.Parse(parts2.Last());
					for (int ordinal2 = 0; ordinal2 < parse2; ordinal2++)
					{
						parser.AssertGetNextLine(out textValue2);
					}
				}
				string textValue10;
				while (parser.CheckKey("Breach Loc", out textValue10))
				{
					string[] parts3 = textValue10.Split(",");
					if (parts3.Length != 5 && parts3.Length != 4)
					{
						pr.ReportError("Unexpected format for Breach Location: " + textValue10);
						return BAD;
					}
					new RRS(parts3[0], parts3[1], parts3[2]);
					parser.CheckKey("Breach Method", out textValue2);
					parser.CheckKey("Breach Geom", out textValue2);
					parser.CheckKey("Breach Start", out textValue2);
					parser.CheckKey("Breach Progression", out int ordinal3);
					if (!parser.ReadFloatLinesXY(ordinal3, out var _, out var _))
					{
						pr.ReportError("Bad float data for Breach Progression");
						return BAD;
					}
					parser.CheckKey("Simplified Physical Breach Downcutting", out int ordinal4);
					if (!parser.ReadFloatLinesXY(ordinal4, out var _, out var _))
					{
						pr.ReportError("Bad float data for Simplified Physical Breach Downcutting");
						return BAD;
					}
					parser.CheckKey("Simplified Physical Breach Widening", out int ordinal5);
					if (!parser.ReadFloatLinesXY(ordinal5, out var _, out var _))
					{
						pr.ReportError("Bad float data for Simplified Physical Breach Widening");
						return BAD;
					}
					parser.CheckKey("Starting Notch Depth", out textValue2);
					parser.CheckKey("Initial Piping Diameter", out textValue2);
					parser.CheckKey("Mass Wasting Options", out textValue2);
					parser.CheckKey("Mass Wasting Width", out textValue2);
					parser.CheckKey("Mass Wasting Duration", out textValue2);
					parser.CheckKey("Mass Wasting Final Bottom Elevation", out textValue2);
					parser.CheckKey("Breach Refill", out textValue2);
					parser.CheckKey("Breach Use User Defined Growth Ratio", out textValue2);
					parser.CheckKey("Breach User Defined Growth Ratio", out textValue2);
					parser.CheckKey("Breach Calculator Data", out textValue2);
					parser.CheckKey("DLBreach Methods", out textValue2);
					parser.CheckKey("DLBreach Coeff", out textValue2);
					parser.CheckKey("DLBreach nValue", out textValue2);
					parser.CheckKey("DLBreach SoilType", out textValue2);
					parser.CheckKey("DLBreach Soil Properties", out textValue2);
					parser.CheckKey("DLBreach Core Dimensions", out textValue2);
					parser.CheckKey("DLBreach Core SoilType", out textValue2);
					parser.CheckKey("DLBreach Core Soil Properties", out textValue2);
					parser.CheckKey("DLBreach Cover Option", out textValue2);
					parser.CheckKey("DLBreach Cover Soil Properties", out textValue2);
					parser.CheckKey("DLBreach Cover Thickness", out textValue2);
					parser.CheckKey("DLBreach Cover Slope", out textValue2);
					parser.CheckKey("DLBreach Breach Direction", out textValue2);
					parser.CheckKey("DLBreach Embankment Width", out textValue2);
					parser.CheckKey("DLBreach Embankment Height", out textValue2);
				}
				parser.SkipLines((string s) => s.StartsWith("Calibration"));
				parser.SkipLines((string s) => s.StartsWith("Ungaged Area"));
				parser.SkipLines((string s) => s.StartsWith("WQ"));
				if (parser.EOF)
				{
					return FileState.Good;
				}
				parser.CheckKey("Sorting and Armoring Iterations", (int s) =>
				{
					SedimentTolerance.SortingAndArmoringIterations = s;
				});
				parser.CheckKey("XS Update Threshold", (string s) =>
				{
					SedimentTolerance.XSUpdateThreshold = s;
				});
				parser.CheckKey("Bed Roughness Predictor", (string s) =>
				{
					SedimentTolerance.BedRoughnessPredictor = s;
				});
				if (parser.CheckKey("Number of Roughness Reaches", out int count))
				{
					for (int ordinal6 = 0; ordinal6 < count; ordinal6++)
					{
						parser.AssertKey("Roughness Reach", out string textValue11);
						SedimentTolerance.RoughnessReach.Add(textValue11);
					}
				}
				parser.CheckKey("Hydraulics Update Threshold", (string s) =>
				{
					SedimentTolerance.HydraulicsUpdateThreshold = s;
				});
				parser.CheckKey("Energy Slope Method", (string s) =>
				{
					SedimentTolerance.EnergySlopeMethod = s;
				});
				parser.CheckKey("Volume Change Method", (string s) =>
				{
					SedimentTolerance.VolumeChangeMethod = s;
				});
				parser.CheckKey("Sediment Retention Method", (string s) =>
				{
					SedimentTolerance.SedimentRetentionMethod = s;
				});
				parser.CheckKey("Sediment TS Multiplier", (string s) =>
				{
					SedimentTolerance.TsMultiplier = s;
				});
				parser.CheckKey("Warm Up Method", (string s) =>
				{
					SedimentTolerance.WarmupMethod = s;
				});
				parser.CheckKey("Warm Up Duration", (float s) =>
				{
					SedimentTolerance.WarmupDuration = s;
				});
				parser.CheckKey("Warm Up Duration - Concentration", (float s) =>
				{
					SedimentTolerance.WarmupDurationConc = s;
				});
				parser.CheckKey("Warm Up Duration - Gradation", (float s) =>
				{
					SedimentTolerance.WarmupDurationGrad = s;
				});
				parser.CheckKey("Warm Up Duration - Bathymetry", (float s) =>
				{
					SedimentTolerance.WarmupDurationBath = s;
				});
				parser.CheckKey("XS Weighting Method", (string s) =>
				{
					SedimentTolerance.XSWeightingMethod = s;
				});
				parser.CheckKey("Number of US Weighted Cross Sections", (int s) =>
				{
					SedimentTolerance.NumUsWeightedXs = s;
				});
				parser.CheckKey("Number of DS Weighted Cross Sections", (int s) =>
				{
					SedimentTolerance.NumDsWeightedXs = s;
				});
				parser.CheckKey("Upstream XS Weight", (float s) =>
				{
					SedimentTolerance.UpstreamXsWeight = s;
				});
				parser.CheckKey("Main XS Weight", (float s) =>
				{
					SedimentTolerance.MainXsWeight = s;
				});
				parser.CheckKey("Downstream XS Weight", (float s) =>
				{
					SedimentTolerance.DownstreamXsWeight = s;
				});
				parser.CheckKey("Number of DS XS's Weighted with US Boundary", (int s) =>
				{
					SedimentTolerance.NumDsXsWeightedWIthUsBd = s;
				});
				parser.CheckKey("Upstream Boundary Weight", (string s) =>
				{
					SedimentTolerance.UsBdWeight = s;
				});
				parser.CheckKey("Weight of XSs Associated with US Boundary", out textValue2);
				parser.CheckKey("Number of US XS's Weighted with DS Boundary", out textValue2);
				parser.CheckKey("Downstream Boundary Weight", out textValue2);
				parser.CheckKey("Weight of XSs Associated with DS Boundary", out textValue2);
				parser.CheckKey("Percentile Method", out textValue2);
				parser.CheckKey("Sediment Output Level", out textValue2);
				parser.CheckKey("Output Increment", out textValue2);
				while (parser.CheckKey("Sediment Output Variables", out textValue2))
				{
				}
				parser.CheckKey("Mass or Volume Output", out textValue2);
				parser.CheckKey("Output Increment Type", out textValue2);
				parser.CheckKey("Profile and TS Output Increment", out textValue2);
				parser.CheckKey("Transport Output Increment", out textValue2);
				parser.SkipLines((string s) => s.StartsWith("Transport Output Increment"));
				parser.CheckKey("XS Output Flag", out textValue2);
				parser.CheckKey("XS Output Increment", out textValue2);
				parser.CheckKey("Disregard all LS", out textValue2);
				parser.CheckKey("Disregard all Conn", out textValue2);
				parser.CheckKey("Disregard all Breaches", out textValue2);
				parser.CheckKey("Disregard all Pumps", out textValue2);
				parser.CheckKey("Read HDF5 Sediment Hotstart", out textValue2);
				parser.CheckKey("Sediment Hotstart Type", out textValue2);
				parser.CheckKey("Sediment Hotstart File", out textValue2);
				parser.CheckKey("Sediment Hotstart Date", out textValue2);
				parser.CheckKey("Sediment Hotstart Time", out textValue2);
				parser.CheckKey("Write Gradation File", out textValue2);
				parser.CheckKey("Read Gradation Hotstart", out textValue2);
				parser.CheckKey("Gradation File Name", out textValue2);
				parser.CheckKey("Write HDF5 File", out textValue2);
				parser.CheckKey("Write Binary Output", out textValue2);
				parser.CheckKey("Write DSS Sediment File", out textValue2);
				parser.CheckKey("DSS Sediment Output Type", out textValue2);
				while (parser.CheckKey("DSS Location", out textValue2))
				{
				}
				while (parser.CheckKey("Summary Reach", out textValue2))
				{
				}
				parser.CheckKey("SV Curve", out textValue2);
				parser.CheckKey("SV Upstream XS", out textValue2);
				parser.CheckKey("SV Downstream XS", out textValue2);
				while (parser.CheckKey("SV Date", out textValue2))
				{
				}
				while (parser.CheckKey("SV Elevation", out textValue2))
				{
				}
				parser.CheckKey("Specific Gage Flag", out textValue2);
				parser.CheckKey("Specific Gage File", out textValue2);
				while (parser.CheckKey("Specific Gage Date", out textValue2))
				{
				}
				while (parser.CheckKey("Specific Gage Times", out textValue2))
				{
				}
				parser.CheckKey("Subcell Erosion Methods", out textValue2);
				parser.CheckKey("Subcell Deposition Methods", out textValue2);
				parser.CheckKey("Advection Scheme", out textValue2);
				parser.CheckKey("Matrix Solver", out textValue2);
				parser.CheckKey("Implicit Weighting Factor", out textValue2);
				parser.CheckKey("Maximum Outer Loop Convergence Iterations", out textValue2);
				parser.CheckKey("Convergence Maximum Absolute", out textValue2);
				parser.CheckKey("Convergence RMSE", out textValue2);
				parser.CheckKey("Grain Fractions Max Abs Error", out textValue2);
				parser.CheckKey("Max Subgrid Regions", out textValue2);
				parser.CheckKey("Max Subgrid Length Scale", out textValue2);
				parser.CheckKey("Max Subgrid Lenght Scale", out textValue2);
				parser.CheckKey("Initial Layer Thickness", out textValue2);
				parser.CheckKey("Min Layer Thickness", out textValue2);
				parser.CheckKey("Max Layer Thickness", out textValue2);
				parser.CheckKey("Number of Layers", out textValue2);
				parser.SkipLines((string s) => s.StartsWith("Dredge"));
				parser.CheckKey("Parameter Uncertainty XML", out textValue2);
				parser.CheckKey("MODFLOW Filename", out textValue2);
				parser.CheckKey("ADH Filename", out textValue2);
				parser.CheckKey("ADH Computation Mode", out textValue2);
				parser.SkipLines((string s) => s.StartsWith("ADH Link"));
				parser.SkipLines((string s) => s.StartsWith("Extra Computation Commands"));
				if (parser.EOF)
				{
					return FileState.Good;
				}
				continue;
			}
			return BAD;
		}
		while (lineIndex != parser.LineIndex);
		return BAD;
	}

	private static bool TryParseDateRange(string textValue, out DateTime dtStart, out DateTime dtEnd)
	{
		dtStart = DateTime.MinValue;
		dtEnd = DateTime.MinValue;
		string[] parts = textValue.Split(',');
		if (parts.Length != 4)
		{
			return false;
		}
		if (parts[1].Contains(":"))
		{
			parts[1] = parts[1].Replace(":", "");
		}
		if (parts[3].Contains(":"))
		{
			parts[3] = parts[3].Replace(":", "");
		}
		if (parts[0].Length != 9 || parts[2].Length != 9)
		{
			dtStart = DateTime.MinValue;
			dtEnd = DateTime.MinValue;
			return true;
		}
		if (parts[1].Length != 4)
		{
			parts[1] = "0000";
		}
		if (parts[3].Length != 4)
		{
			parts[3] = "0000";
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[0]).Slice(0, 2), out var result))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[2]).Slice(0, 2), out var result2))
		{
			return false;
		}
		if (!TryParseMonth(((ReadOnlySpan<char>)parts[0]).Slice(2, 3), out var month))
		{
			return false;
		}
		if (!TryParseMonth(((ReadOnlySpan<char>)parts[2]).Slice(2, 3), out var month2))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[0]).Slice(5, 4), out var result3))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[2]).Slice(5, 4), out var result4))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[1]).Slice(0, 2), out var result5))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[3]).Slice(0, 2), out var result6))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[1]).Slice(2, 2), out var result7))
		{
			return false;
		}
		if (!int.TryParse(((ReadOnlySpan<char>)parts[3]).Slice(2, 2), out var result8))
		{
			return false;
		}
		dtStart = new DateTime(result3, month, result, 0, result7, 0);
		dtStart = dtStart.AddHours(result5);
		dtEnd = new DateTime(result4, month2, result2, 0, result8, 0);
		dtEnd = dtEnd.AddHours(result6);
		return true;
	}

	private static bool TryParseMonth(ReadOnlySpan<char> chars, out int month)
	{
		switch (chars.ToString().ToUpper())
		{
		case "JAN":
			month = 1;
			break;
		case "FEB":
			month = 2;
			break;
		case "MAR":
			month = 3;
			break;
		case "APR":
			month = 4;
			break;
		case "MAY":
			month = 5;
			break;
		case "JUN":
			month = 6;
			break;
		case "JUL":
			month = 7;
			break;
		case "AUG":
			month = 8;
			break;
		case "SEP":
			month = 9;
			break;
		case "OCT":
			month = 10;
			break;
		case "NOV":
			month = 11;
			break;
		case "DEC":
			month = 12;
			break;
		default:
			month = -1;
			return false;
		}
		return true;
	}

	public static bool IsValidFilename(string flowFilename)
	{
		string extension = Path.GetExtension(flowFilename);
		if (extension.Length != 4)
		{
			return false;
		}
		if (extension[1] != 'p')
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

	public override string ToString()
	{
		return ShortID;
	}
}
