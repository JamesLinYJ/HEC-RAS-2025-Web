using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ras.Layers.Computes;

public class RunTimeModelCoefficients
{
	private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public int SchemaVersion { get; set; } = 1;

	public double MachineFactor { get; set; } = 1.0;

	public Dictionary<string, double> StepCostC { get; set; } = new Dictionary<string, double>();

	public Dictionary<string, double> AlphaByFamily { get; set; } = new Dictionary<string, double>();

	public double Beta { get; set; }

	public double AdvectionMultiplier { get; set; } = 1.0;

	public double TurbulenceMultiplier { get; set; } = 1.0;

	public Dictionary<string, double> InitBaseSeconds { get; set; } = new Dictionary<string, double>();

	public Dictionary<string, double> InitPerCellSeconds { get; set; } = new Dictionary<string, double>();

	public double OutputPerCellSeconds { get; set; }

	public Dictionary<string, double> LogErrorFactorByFamily { get; set; } = new Dictionary<string, double>();

	public double AdaptiveDtHighFactor { get; set; } = 1.5;

	public string CalibrationNote { get; set; } = "";

	public static RunTimeModelCoefficients Default()
	{
		return new RunTimeModelCoefficients
		{
			MachineFactor = 1.0,
			StepCostC = new Dictionary<string, double>
			{
				["SWE-Exp-FWDBACK"] = 1.91725E-07,
				["SWE-Exp-FWDBACKFWD"] = 1.77607E-07,
				["SWE-Exp-FWDBACKFWD_LTS"] = 2.70547E-07,
				["SWE-Imp/Pardiso"] = 1.02227E-07,
				["SWE-Imp/PcgManaged"] = 2.81484E-08,
				["DWE/Pardiso"] = 1.2671E-07
			},
			AlphaByFamily = new Dictionary<string, double>
			{
				["SWE-Explicit"] = 1.029191,
				["SWE-Implicit"] = 1.194307,
				["DWE"] = 1.193228
			},
			Beta = -0.416187,
			AdvectionMultiplier = 1.266696,
			TurbulenceMultiplier = 1.471125,
			InitBaseSeconds = new Dictionary<string, double>
			{
				["SWE-Explicit"] = 0.0,
				["SWE-Implicit"] = 0.0,
				["DWE"] = 0.0
			},
			InitPerCellSeconds = new Dictionary<string, double>
			{
				["SWE-Explicit"] = 2.55736E-06,
				["SWE-Implicit"] = 5.12622E-06,
				["DWE"] = 5.43007E-06
			},
			OutputPerCellSeconds = 0.0,
			LogErrorFactorByFamily = new Dictionary<string, double>
			{
				["SWE-Explicit"] = 1.192538,
				["SWE-Implicit"] = 1.222698,
				["DWE"] = 1.121237
			},
			AdaptiveDtHighFactor = 1.5,
			CalibrationNote = "SANCHEZ-VM01 (8 cores) | 21f5c18 | 2026-07-17 | 24 baseline configs, 66 observations"
		};
	}

	public static bool TryLoad(string path, out RunTimeModelCoefficients coefficients)
	{
		coefficients = null;
		try
		{
			if (!File.Exists(path))
			{
				return false;
			}
			coefficients = JsonSerializer.Deserialize<RunTimeModelCoefficients>(File.ReadAllText(path), _jsonOptions);
			return coefficients != null;
		}
		catch (Exception)
		{
			coefficients = null;
			return false;
		}
	}

	public void Save(string path)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
		File.WriteAllText(path, JsonSerializer.Serialize(this, _jsonOptions));
	}
}
