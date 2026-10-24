using System;
using Ras.Engine;
using Ras.Output;

namespace Ras.Layers;

public class ResultReporter
{
	private static void Track(Solver solver, MemoryOutputBlock ob, Action<Action> syncContext, int reportEveryNTimesteps)
	{
		int tsCount = 0;
		solver.AddTimestepDone((MeshTimestep mt) =>
		{
			if (tsCount % reportEveryNTimesteps == 0)
			{
				MeshTimestep wsClone = mt.Clone(fullClone: true);
				syncContext(() =>
				{
					ob.AddTimestep(wsClone);
				});
			}
			tsCount++;
		});
	}
}
