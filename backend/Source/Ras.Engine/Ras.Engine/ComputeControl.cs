using System;
using System.Collections.Generic;
using Utility.Units;

namespace Ras.Engine;

public class ComputeControl
{
	[Flags]
	public enum CellFlag
	{
		None = 0,
		StreamwiseCoordinates = 1
	}

	public const UnitSystem DefaultUnits = UnitSystem.SI;

	private readonly UnitSystem _units = UnitSystem.SI;

	public const double FtToM = 0.3048;

	public double LToSI;

	public double MtoSI;

	public const double GravSI = 9.80665025024414;

	public double grav = 9.80665025024414;

	public double WaterDensity = 1000.0;

	public double AirDensity = 1.225;

	public long DtMicros;

	public long EndMicros;

	public bool variableDt = true;

	public long dtMinMicros = 10000L;

	public int nLookahead = 8;

	public double dtMaxIncrease = 0.05;

	public bool accumulateStepLimitation = true;

	public int maxIterations = 15;

	public bool usePeriodAverageFlows;

	public readonly Dictionary<string, CellFlag> CellFlagMap;

	public UnitSystem Units => _units;

	public double nSItoUnits
	{
		get
		{
			if (_units != UnitSystem.SI)
			{
				return Math.Pow(0.3048, 1.0 / 3.0);
			}
			return 1.0;
		}
	}

	public double Timestep
	{
		get
		{
			return ComputeDriver.MicrosToSeconds(DtMicros);
		}
		set
		{
			DtMicros = ComputeDriver.SecondsToMicros(value);
		}
	}

	public double EndTime
	{
		get
		{
			return ComputeDriver.MicrosToSeconds(EndMicros);
		}
		set
		{
			EndMicros = ComputeDriver.SecondsToMicros(value);
		}
	}

	public double dtMin => ComputeDriver.MicrosToSeconds(dtMinMicros);

	public ComputeControl(UnitSystem units = UnitSystem.SI)
	{
		CellFlagMap = new Dictionary<string, CellFlag>(StringComparer.OrdinalIgnoreCase);
		CellFlagMap["StreamwiseCoordinates"] = CellFlag.StreamwiseCoordinates;
		if (units == UnitSystem.NullUnitSystem)
		{
			units = UnitSystem.SI;
		}
		_units = units;
		if (_units == UnitSystem.USCustomary)
		{
			LToSI = 0.3048;
			MtoSI = 0.068521765562;
			grav /= LToSI;
			WaterDensity /= MtoSI * LToSI * LToSI * LToSI;
			AirDensity /= MtoSI * LToSI * LToSI * LToSI;
		}
		else
		{
			LToSI = 1.0;
			MtoSI = 1.0;
		}
		DtMicros = 1000000L;
	}
}
