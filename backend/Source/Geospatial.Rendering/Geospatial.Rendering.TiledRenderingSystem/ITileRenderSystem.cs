using System.Collections.Generic;
using System.Threading.Tasks;
using Utility.DataStructures;

namespace Geospatial.Rendering.TiledRenderingSystem;

public interface ITileRenderSystem<T> where T : IRenderTask
{
	void InitializeRenderingSystem(int maxThreads = int.MaxValue);

	void ShutdownRenderingSystem();

	void QueueRenderTasks(IList<T> tasks);

	void QueueRenderTask(T task)
	{
		QueueRenderTasks(new CompactList<T>(task));
	}

	ValueTask RenderTasksAsync(IList<T> tasks, bool renderSerially = false);

	void ForceCancelAllWork();
}
