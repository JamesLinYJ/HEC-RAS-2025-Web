using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ras.Layers;
using Utility.ORM;

namespace Ras.Process.RasCommands.Explorer.PlanEditor;

public static class PlanPropertyExtractor
{
	private static readonly Dictionary<string, string> PropertyDescriptions = new Dictionary<string, string>
	{
		["ComputeTimestep"] = "Base timestep for solver synchronization. LTS levels must be integer divisors of this.",
		["BaseOutputInterval"] = "Interval between output result snapshots. Must be multiple of compute timestep.",
		["SolverType"] = "CPU uses multi-threaded processing, GPU uses CUDA acceleration.",
		["EquationSet"] = "SWE (Shallow Water Equations) or DWE (Diffusion Wave Equations).",
		["MaxCourant"] = "Maximum Courant number limit. Higher values allow larger timesteps but may reduce stability.",
		["TimestepSafetyFactor"] = "Safety factor applied to timestep calculation. Lower values are more conservative.",
		["UseMinAdvectiveTimeStep"] = "Enables minimum advective time step.",
		["MinAdvectiveTimeStep"] = "Minimum time step for advective time-step criteria.",
		["Theta"] = "Implicit weighting factor. 1.0 = fully implicit, 0.0 = fully explicit.",
		["CoreCount"] = "Number of CPU cores for parallel computation.",
		["ParallelMethod"] = "Threading strategy for parallel computation.",
		["LtsLevels"] = "Local time stepping levels. More levels can improve performance but add complexity.",
		["UseFBF"] = "Enable Forward-Backward-Forward stepping scheme.",
		["UseAdvection"] = "Enable momentum advection terms in the solver.",
		["AdvectionMethod"] = "Method for computing advection terms.",
		["AdvectionImplicitFactor"] = "Implicit factor for advection computation (0-1).",
		["ImplicitFactor"] = "Implicit factor for water surface and velocity computation (0.5-1).",
		["UseDiffusion"] = "Enable turbulence in the solver. Leaves the model and coefficients untouched.",
		["TurbulenceModel"] = "Closure supplying the eddy viscosity: Smagorinsky, k-epsilon or Prandtl k-l.",
		["BaseEddyViscosity"] = "Base eddy viscosity coefficient for diffusion.",
		["LongMixCoeff"] = "Longitudinal mixing coefficient for turbulent diffusion.",
		["TransMixCoeff"] = "Transverse mixing coefficient for turbulent diffusion.",
		["HorizMixCoeff"] = "Smagorinsky coefficient for horizontal mixing.",
		["Latitude"] = "Latitude for Coriolis force calculation (-90 to 90 degrees).",
		["TimeWindowMode"] = "Use Plan's custom time window or inherit from Boundary Condition.",
		["InternalStartTime"] = "Compute start time (only used when TimeWindowMode is Plan).",
		["InternalComputeDurationSeconds"] = "Compute duration in seconds (only used when TimeWindowMode is Plan).",
		["UseGPUAdaptive"] = "Enable GPU adaptive timestep for GPU solver.",
		["SensitivityRun"] = "Mark this plan as a sensitivity analysis run.",
		["ShortID"] = "Short identifier for labeling plots. Does not need to be unique."
	};

	private static readonly Dictionary<string, (double min, double max)> PropertyRanges = new Dictionary<string, (double, double)>
	{
		["MaxCourant"] = (0.1, 5.0),
		["TimestepSafetyFactor"] = (0.01, 0.95),
		["MinAdvectiveTimeStep"] = (-10000.0, 600.0),
		["Theta"] = (0.0, 1.0),
		["AdvectionImplicitFactor"] = (0.0, 1.0),
		["ImplicitFactor"] = (0.5, 1.0),
		["Latitude"] = (-90.0, 90.0),
		["BaseEddyViscosity"] = (0.0, 100.0),
		["LongMixCoeff"] = (0.0, 3.0),
		["TransMixCoeff"] = (0.0, 1.0),
		["HorizMixCoeff"] = (0.0, 0.5),
		["CoreCount"] = (1.0, 128.0)
	};

	private static readonly HashSet<string> ExcludedProperties = new HashSet<string> { "ComputeBaseTimestepSeconds", "ComputeDurationSeconds", "ComputeDurationTimeSpan", "StartTime", "EndTime", "UseGPU", "ExpectedProfileCount" };

	public static List<PlanPropertyInfo> GetEditableProperties(Plan plan)
	{
		List<PlanPropertyInfo> planPropertyInfos = new List<PlanPropertyInfo>();
		PropertyInfo[] properties = typeof(Plan).GetProperties(BindingFlags.Instance | BindingFlags.Public);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (ExcludedProperties.Contains(propertyInfo.Name))
			{
				continue;
			}
			MetadataAttribute customAttribute = propertyInfo.GetCustomAttribute<MetadataAttribute>();
			if (customAttribute != null && propertyInfo.CanWrite)
			{
				PlanPropertyInfo planPropertyInfo = new PlanPropertyInfo
				{
					DisplayName = (customAttribute.KeyName ?? propertyInfo.Name),
					PropertyName = propertyInfo.Name,
					ReflectionInfo = propertyInfo,
					ValueType = propertyInfo.PropertyType,
					Description = GetDescription(propertyInfo.Name),
					IsReadOnly = false
				};
				if (PropertyRanges.TryGetValue(propertyInfo.Name, out (double, double) valueTuple))
				{
					planPropertyInfo.MinValue = valueTuple.Item1;
					planPropertyInfo.MaxValue = valueTuple.Item2;
				}
				planPropertyInfos.Add(planPropertyInfo);
			}
		}
		return SortProperties(planPropertyInfos);
	}

	private static string GetDescription(string propertyName)
	{
		if (!PropertyDescriptions.TryGetValue(propertyName, out var localValue))
		{
			return "No description available.";
		}
		return localValue;
	}

	private static List<PlanPropertyInfo> SortProperties(List<PlanPropertyInfo> properties)
	{
		string[] source = new string[29]
		{
			"ComputeTimestep", "BaseOutputInterval", "TimeWindowMode", "InternalStartTime", "InternalComputeDurationSeconds", "SolverType", "CoreCount", "ParallelMethod", "UseGPUAdaptive", "EquationSet",
			"MaxCourant", "TimestepSafetyFactor", "MinAdvectiveTimeStep", "Theta", "LtsLevels", "UseFBF", "UseAdvection", "AdvectionMethod", "AdvectionImplicitFactor", "ImplicitFactor",
			"UseDiffusion", "TurbulenceModel", "BaseEddyViscosity", "LongMixCoeff", "TransMixCoeff", "HorizMixCoeff", "Latitude", "ShortID", "SensitivityRun"
		};
		Dictionary<string, int> orderMap = source.Select((string name, int index) => (name: name, index: index)).ToDictionary(((string name, int index) x) => x.name, ((string name, int index) x) => x.index);
		return properties.OrderBy((PlanPropertyInfo p) => (!orderMap.TryGetValue(p.PropertyName, out var localValue)) ? 999 : localValue).ToList();
	}
}
