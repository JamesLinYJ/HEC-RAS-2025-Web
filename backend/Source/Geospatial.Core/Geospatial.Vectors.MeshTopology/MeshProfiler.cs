using System;
using System.Diagnostics;
using System.Text;
using Geospatial.Vectors.Topology;

namespace Geospatial.Vectors.MeshTopology;

public class MeshProfiler
{
	public enum MeshTask
	{
		BuildMeshTotal,
		BuildConceptualMesh,
		BuildMesh,
		DiscretizeRegion,
		ComputeLaplaceAll,
		LaplaceBuildTin,
		ComputeLaplaceOne,
		BuildLaplace,
		SolveLaplace,
		GenerateTriangular,
		GenerateQuad,
		GenerateCartesian,
		GenerateOneD,
		PostprocessRegion,
		PostprocessGlobal,
		GlobalSizeFunction,
		_TaskCount
	}

	private int[] _taskWorkers;

	private int[] _taskWorkersHighwater;

	private int[] _taskCounts;

	private double[] _taskElapsed;

	private long[] _taskLastTicks;

	private static readonly Lazy<MeshProfiler> lazy = new Lazy<MeshProfiler>(() => new MeshProfiler());

	private readonly object _lock = new object();

	public static MeshProfiler Instance => lazy.Value;

	public int TaskCount(MeshTask task)
	{
		return _taskCounts[(int)task];
	}

	public double TaskSeconds(MeshTask task)
	{
		return _taskElapsed[(int)task] / (double)Stopwatch.Frequency;
	}

	private MeshProfiler()
	{
		int count = 16;
		_taskElapsed = new double[count];
		_taskWorkers = new int[count];
		_taskCounts = new int[count];
		_taskWorkersHighwater = new int[count];
		_taskLastTicks = new long[count];
		long timestamp = Stopwatch.GetTimestamp();
		for (int i = 0; i < count; i++)
		{
			_taskLastTicks[i] = timestamp;
		}
	}

	public void Reset()
	{
		long timestamp = Stopwatch.GetTimestamp();
		for (int i = 0; i < 16; i++)
		{
			_taskLastTicks[i] = timestamp;
			_taskElapsed[i] = 0.0;
			_taskWorkers[i] = 0;
			_taskCounts[i] = 0;
			_taskWorkersHighwater[i] = 0;
		}
	}

	public static MeshTask PatchTypeToTask(PatchType patchType)
	{
		return patchType switch
		{
			PatchType.TrianglePaver => MeshTask.GenerateTriangular, 
			PatchType.Quad => MeshTask.GenerateQuad, 
			PatchType.Cartesian => MeshTask.GenerateCartesian, 
			PatchType.OneD => MeshTask.GenerateOneD, 
			_ => throw new ArgumentOutOfRangeException(patchType.ToString()), 
		};
	}

	public void StartGenerateTask(PatchType patchType)
	{
		StartTask(PatchTypeToTask(patchType));
	}

	public void StopGenerateTask(PatchType patchType)
	{
		StopTask(PatchTypeToTask(patchType));
	}

	public void StartTask(MeshTask task)
	{
		long timestamp = Stopwatch.GetTimestamp();
		lock (_lock)
		{
			long timestampTaskLastTicksTerm = timestamp - _taskLastTicks[(int)task];
			_taskElapsed[(int)task] += timestampTaskLastTicksTerm * _taskWorkers[(int)task];
			_taskLastTicks[(int)task] = timestamp;
			_taskWorkers[(int)task]++;
			_taskCounts[(int)task]++;
			_taskWorkersHighwater[(int)task] = Math.Max(_taskWorkersHighwater[(int)task], _taskWorkers[(int)task]);
		}
	}

	public void StopTask(MeshTask task)
	{
		long timestamp = Stopwatch.GetTimestamp();
		lock (_lock)
		{
			long timestampTaskLastTicksTerm = timestamp - _taskLastTicks[(int)task];
			_taskElapsed[(int)task] += timestampTaskLastTicksTerm * _taskWorkers[(int)task];
			_taskLastTicks[(int)task] = timestamp;
			_taskWorkers[(int)task]--;
		}
	}

	public string Summary()
	{
		StringBuilder stringBuilder = new StringBuilder("Timing summary\n");
		for (int i = 0; i < _taskWorkers.Length; i++)
		{
			_003C_003Ey__InlineArray4<object> buffer = default;
			ref object reference = ref buffer[0];
			MeshTask meshTask = (MeshTask)i;
			reference = meshTask.ToString();
			buffer[1] = _taskElapsed[i] / (double)Stopwatch.Frequency;
			buffer[2] = _taskCounts[i];
			buffer[3] = _taskWorkersHighwater[i];
			stringBuilder.AppendFormat("{0}: {1:F4}s {2} tasks, {3} max concurrent\n", (ReadOnlySpan<object?>)buffer);
			if (_taskWorkers[i] != 0)
			{
				stringBuilder.AppendFormat("   warning: ended with worker count {0}!\n", _taskWorkers[i]);
			}
		}
		stringBuilder.Append("Release build");
		return stringBuilder.ToString();
	}

	public string BriefSummary()
	{
		StringBuilder stringBuilder = new StringBuilder();
		double taskElapsed = _taskElapsed[0];
		stringBuilder.AppendFormat("Mesh generation time: {0:F2}s total\n", taskElapsed / (double)Stopwatch.Frequency);
		double computed = 0.0;
		computed += _taskElapsed[3];
		computed += _taskElapsed[4];
		double taskElapsed2 = _taskElapsed[2];
		double taskElapsed3 = _taskElapsed[10];
		double taskElapsed4 = _taskElapsed[9];
		double taskElapsed5 = _taskElapsed[11];
		double taskElapsed6 = _taskElapsed[13];
		double taskElapsed3TaskElapsed4Term = taskElapsed3 + taskElapsed4 + taskElapsed5 + taskElapsed6;
		double taskElapsed2TaskElapsed4Term = 0.0;
		double taskElapsed2TaskElapsed5Term = 0.0;
		double taskElapsed2TaskElapsed6Term = 0.0;
		if (taskElapsed3TaskElapsed4Term > 0.0)
		{
			computed += taskElapsed2 * taskElapsed3 / taskElapsed3TaskElapsed4Term;
			taskElapsed2TaskElapsed4Term = taskElapsed2 * taskElapsed4 / taskElapsed3TaskElapsed4Term;
			taskElapsed2TaskElapsed5Term = taskElapsed2 * taskElapsed5 / taskElapsed3TaskElapsed4Term;
			taskElapsed2TaskElapsed6Term = taskElapsed2 * taskElapsed6 / taskElapsed3TaskElapsed4Term;
		}
		if (taskElapsed > 0.0)
		{
			stringBuilder.AppendFormat("Quads: {0:F1}%\n", 100.0 * computed / taskElapsed);
			stringBuilder.AppendFormat("Triangles: {0:F1}%\n", 100.0 * taskElapsed2TaskElapsed4Term / taskElapsed);
			stringBuilder.AppendFormat("Cartesian: {0:F1}%\n", 100.0 * taskElapsed2TaskElapsed5Term / taskElapsed);
			stringBuilder.AppendFormat("Postprocessing: {0:F1}%\n", 100.0 * taskElapsed2TaskElapsed6Term / taskElapsed);
			double taskElapsedTerm = taskElapsed - computed - taskElapsed2TaskElapsed4Term - taskElapsed2TaskElapsed5Term - taskElapsed2TaskElapsed6Term;
			stringBuilder.AppendFormat("Global pre/postprocessing: {0:F1}%\n", 100.0 * taskElapsedTerm / taskElapsed);
		}
		return stringBuilder.ToString();
	}
}
