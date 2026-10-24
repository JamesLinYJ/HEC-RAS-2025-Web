using System.Collections.Generic;
using System.Linq;
using Geospatial.Vectors;
using Ras.Engine.Structures.Culverts;

namespace Ras.Engine;

public class MeshTimestep
{
	public readonly MeshTimestepStats Stats;

	public double Time => Stats.Time;

	public double TimeStep => Stats.TimeStep;

	public long Step => Stats.Step;

	public int Retries => Stats.Retries;

	public int Iterations => Stats.Iterations;

	public int LimitingCell => Stats.LimitingCell;

	public int LimitingFace => Stats.LimitingFace;

	public MeshWaterState WaterState { get; set; }

	[Expose(MeshElementType.Cell)]
	public double[] CellDtMax { get; set; }

	[Expose(MeshElementType.Cell)]
	public int[] CellLimitCount { get; private set; }

	public CulvertTimestepData CulvertData { get; set; }

	public MeshTimestep(MeshWaterState state, ComputeDriver driver)
	{
		WaterState = state;
		Stats = new MeshTimestepStats
		{
			Step = driver.StepCount,
			Time = ComputeDriver.MicrosToSeconds(WaterState.tMicros),
			TimeStep = ComputeDriver.MicrosToSeconds(WaterState.dtMicros),
			MaxTimeStep = ComputeDriver.MicrosToSeconds(driver.DtMaxMicros),
			WetCellCount = (WaterState?.GetWetCellCount() ?? 0),
			Retries = driver.globalRetryCount,
			Iterations = driver.globalIterationCount,
			LimitingCell = driver.globalLimitingCell,
			LimitingFace = driver.globalLimitingFace,
			ActiveCells = (float)driver.globalActiveCells,
			AdvectionLimitCount = driver.globalStepLimitationCount[3],
			UserLimitCount = driver.globalStepLimitationCount[2],
			CelerityLimitCount = driver.globalStepLimitationCount[1],
			IncreaseLimitCount = driver.globalStepLimitationCount[5],
			RetryElevationCount = driver.globalStepLimitationCount[8],
			RetryNanCount = driver.globalStepLimitationCount[9],
			RetryNegVolumeCount = driver.globalStepLimitationCount[7]
		};
		CellDtMax = driver.TryGetCellDtMax(state);
		CellLimitCount = driver.cellLimitCount;
	}

	public MeshTimestep(MeshWaterState state, Solver solver)
	{
		WaterState = state;
		Stats = new MeshTimestepStats
		{
			Step = solver.step,
			Time = ComputeDriver.MicrosToSeconds(WaterState.tMicros),
			TimeStep = ComputeDriver.MicrosToSeconds(WaterState.dtMicros),
			WetCellCount = (WaterState?.GetWetCellCount() ?? 0)
		};
		CellDtMax = solver.CellDtMax;
		CellLimitCount = null;
	}

	public MeshTimestep(MeshWaterState state, long step)
	{
		WaterState = state;
		Stats = new MeshTimestepStats
		{
			Step = step,
			Time = ComputeDriver.MicrosToSeconds(WaterState.tMicros),
			TimeStep = ComputeDriver.MicrosToSeconds(WaterState.dtMicros),
			WetCellCount = (WaterState?.GetWetCellCount() ?? 0)
		};
	}

	public MeshTimestep Clone(bool fullClone = false)
	{
		MeshTimestep meshTimestep = ((!fullClone) ? new MeshTimestep(WaterState.Clone(), Step) : new MeshTimestep(WaterState.CloneFull(), Step));
		double computed = 3600.0;
		if (CellDtMax != null)
		{
			meshTimestep.CellDtMax = CellDtMax.ToArray();
			for (int i = 0; i < CellDtMax.Length; i++)
			{
				if (!double.IsFinite(meshTimestep.CellDtMax[i]) || meshTimestep.CellDtMax[i] > computed)
				{
					meshTimestep.CellDtMax[i] = -9999.0;
				}
			}
		}
		meshTimestep.CellLimitCount = CellLimitCount?.ToArray();
		meshTimestep.CulvertData = CulvertData?.Clone();
		return meshTimestep;
	}

	public static List<MeshTimestep> Track(Solver solver, double interval)
	{
		List<MeshTimestep> retn = new List<MeshTimestep>();
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			if (mt.Time == 0.0)
			{
				retn.Clear();
			}
			double scaledTimeStep = mt.TimeStep * 0.5;
			double timeWithInterval = mt.Time % interval;
			if (timeWithInterval < scaledTimeStep || timeWithInterval > interval - scaledTimeStep)
			{
				retn.Add(mt.Clone());
			}
		});
		return retn;
	}

	public static List<MeshTimestep> Track(Solver solver, int steps)
	{
		List<MeshTimestep> retn = new List<MeshTimestep>();
		int tsCount = 0;
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			if (mt.Time == 0.0)
			{
				retn.Clear();
				tsCount = 0;
			}
			if (tsCount % steps == 0)
			{
				retn.Add(mt.Clone());
			}
			tsCount++;
		});
		return retn;
	}

	public static List<MeshTimestep> Track(Solver solver, IReadOnlyList<double> outputTimes)
	{
		double[] sorted = outputTimes.OrderBy((double t) => t).ToArray();
		List<MeshTimestep> retn = new List<MeshTimestep>();
		int nextIdx = 0;
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			if (mt.Time == 0.0)
			{
				retn.Clear();
				nextIdx = 0;
			}
			for (; nextIdx < sorted.Length && mt.Time >= sorted[nextIdx]; nextIdx++)
			{
				retn.Add(mt.Clone());
			}
		});
		return retn;
	}
}
