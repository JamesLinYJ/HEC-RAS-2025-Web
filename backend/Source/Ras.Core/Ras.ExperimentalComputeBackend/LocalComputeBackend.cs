using System;
using System.Threading;
using System.Threading.Tasks;
using Ras.Layers;
using Utility.Logging;

namespace Ras.ExperimentalComputeBackend;

public sealed class LocalComputeBackend : IComputeBackend
{
	public string Name => "Local";

	public ComputeBackendInfo Info => new ComputeBackendInfo
	{
		Id = "local",
		DisplayName = "This Computer",
		IsRemote = false,
		RequiresUpload = false,
		SupportsGpu = true,
		SupportsCpu = true,
		MaxConcurrency = 1,
		SupportsExperiments = false,
		SupportsBatchStatus = false
	};

	public ValueTask<bool> IsAvailableAsync(CancellationToken ct = default(CancellationToken))
	{
		return new ValueTask<bool>(result: true);
	}

	public Task<ComputeOutcome> ComputeAsync(ComputeRequest request, ComputeContext context, CancellationToken ct = default(CancellationToken))
	{
		return Task.Run(() =>
		{
			try
			{
				Result res = request.PreparedResult;
				if (res == null)
				{
					OperationResult operationResult = Result.TryLoad(request.ReadyToRunPath, out res);
					if (!operationResult.Result)
					{
						return ComputeOutcome.Fail(operationResult);
					}
				}
				Action<Action> synchronizationContext = null;
				if (context.UiSync != null)
				{
					synchronizationContext = (Action act) =>
					{
						context.UiSync.Sync(act);
					};
				}
				OperationResult operationResult2 = res.Compute(synchronizationContext, context.Reporter);
				return operationResult2.Result ? ComputeOutcome.Success(res) : ComputeOutcome.Fail(operationResult2);
			}
			catch (Exception ex)
			{
				return ComputeOutcome.Fail(OperationResult.Fail("Compute", ex));
			}
		});
	}
}
