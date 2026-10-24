using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Utility;
using Utility.DataStructures;

namespace Geospatial.Rendering.TiledRenderingSystem;

public class TaskTileRenderSystem<T> where T : IRenderTask
{
	public ISynchronizationContext UIThreadSynchronizationContext { get; private init; }

	public Func<T, ValueTask> RenderTaskCompleted { get; private init; }

	public TaskTileRenderSystem(ISynchronizationContext uiThreadSyncContext, Func<T, ValueTask> renderTaskCompleted)
	{
		UIThreadSynchronizationContext = uiThreadSyncContext;
		RenderTaskCompleted = renderTaskCompleted;
	}

	public void InitializeRenderingSystem()
	{
	}

	public void ShutdownRenderingSystem()
	{
	}

	public async ValueTask QueueTilesToRender(IList<T> tasks, bool renderSimultaneously, bool renderSerially = false)
	{
		await RenderTiles(tasks, renderSimultaneously, RenderTaskCompleted, renderSerially);
	}

	public async ValueTask QueueTileToRender(T task)
	{
		await RenderTiles(new CompactList<T>(task), renderSimultaneously: false, RenderTaskCompleted);
	}

	public static async ValueTask RenderTiles(IList<T> tasks, bool renderSimultaneously, Func<T, ValueTask> taskCompleted, bool renderSerially = false)
	{
		List<Task<T>> runningTasks = new List<Task<T>>();
		for (int i = 0; i < tasks.Count; i++)
		{
			T task = tasks[i];
			Task<T> task2 = Task.Run(() =>
			{
				task.Render();
				return task;
			});
			if (!renderSerially)
			{
				runningTasks.Add(task2);
			}
			else
			{
				await task2;
			}
		}
		if (renderSimultaneously)
		{
			T[] whenAll = await Task.WhenAll(runningTasks);
			foreach (T whenAll2 in whenAll)
			{
				await taskCompleted(whenAll2);
			}
		}
		else
		{
			while (runningTasks.Any())
			{
				Task<T> task3 = await Task.WhenAny(runningTasks);
				runningTasks.Remove(task3);
				T result = task3.Result;
				await taskCompleted(result);
			}
		}
	}

	public void ForceCancelAllWork()
	{
	}
}
