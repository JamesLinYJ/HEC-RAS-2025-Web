using System;
using System.Collections.Generic;
using Ras.Engine;
using Ras.Layers;

namespace Ras.Optimization;

public sealed class CalibrationContext
{
	private readonly IReadOnlyDictionary<string, float> _baselineManning;

	private readonly List<ManningZoneScale> _zoneScales = new List<ManningZoneScale>();

	public TurbulenceParams Turbulence;

	public NVTable ManningTable { get; }

	public IReadOnlyList<ManningZoneScale> ZoneScales => _zoneScales;

	public bool ManningDirty { get; set; }

	public CalibrationContext(IReadOnlyDictionary<string, float> baselineManning, TurbulenceParams baselineTurbulence)
	{
		_baselineManning = baselineManning ?? new Dictionary<string, float>();
		ManningTable = new NVTable();
		foreach (KeyValuePair<string, float> baselineManning2 in _baselineManning)
		{
			ManningTable.Set(baselineManning2.Key, baselineManning2.Value);
		}
		Turbulence = baselineTurbulence;
	}

	public float BaselineManning(string className)
	{
		if (!_baselineManning.TryGetValue(className, out var localValue))
		{
			throw new ArgumentException("Class '" + className + "' is not present in the baseline Manning table.");
		}
		return localValue;
	}

	public void SetZoneScale(ManningZone zone, double factor)
	{
		foreach (ManningZoneScale zoneScale in _zoneScales)
		{
			if (zoneScale.Zone == zone)
			{
				zoneScale.Factor = factor;
				return;
			}
		}
		_zoneScales.Add(new ManningZoneScale(zone, factor));
	}
}
