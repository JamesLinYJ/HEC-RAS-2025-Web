using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;
using Utility.Progress;

namespace Ras.Optimization;

public sealed class InProcessRunExecutor : ICalibrationRunExecutor
{
	private static readonly ILogger _logger = LogHelper.GetLogger("InProcessRunExecutor");

	private readonly ParameterSpace _space;

	private readonly ICalibrationModel _model;

	private readonly ObjectiveFunction _objective;

	private readonly CalibrationControl _control;

	public InProcessRunExecutor(ParameterSpace space, ICalibrationModel model, ObjectiveFunction objective, CalibrationControl control)
	{
		_space = space ?? throw new ArgumentNullException("space");
		_model = model ?? throw new ArgumentNullException("model");
		_objective = objective ?? throw new ArgumentNullException("objective");
		_control = control ?? throw new ArgumentNullException("control");
	}

	public async Task<RunResult> EvaluateAsync(int runId, double[] physical, bool forcePersist, ProgressReporter pr = null)
	{
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		CalibrationContext ctx = _model.CreateContext();
		_space.Apply(physical, ctx);
		bool persist = forcePersist || _control.Retention == ResultRetention.PreserveAll || _control.PersistenceMode == RunPersistenceMode.FullPipeline;
		string outputDirectory = ((persist && !string.IsNullOrEmpty(_control.OutputDirectory)) ? Path.Combine(_control.OutputDirectory, $"run_{runId:D5}") : null);
		RunArtifactOptions artifacts = new RunArtifactOptions
		{
			PersistResult = persist,
			OutputDirectory = outputDirectory
		};
		ModelRunResult modelRunResult;
		double objective;
		GaugeMetricResult[] perGauge;
		try
		{
			modelRunResult = await _model.RunAsync(ctx, runId, artifacts, _control.CoresPerRun, pr);
			if (modelRunResult == null || !modelRunResult.Succeeded || modelRunResult.Result == null)
			{
				return Failed(runId, physical, modelRunResult?.FailureReason ?? "Simulation did not complete.");
			}
			objective = _objective.Evaluate(modelRunResult.Result, out perGauge, pr);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Run {RunId} threw during simulation or objective evaluation.", runId);
			return Failed(runId, physical, ex.Message);
		}
		bool isFinite = double.IsFinite(objective);
		return new RunResult
		{
			RunId = runId,
			Physical = physical,
			Objective = objective,
			PerGauge = perGauge,
			Succeeded = isFinite,
			FailureReason = (isFinite ? null : "Objective could not be evaluated (gauge extraction failed or metric was not finite)."),
			ResultDirectory = modelRunResult.ResultDirectory,
			Preserved = persist
		};
	}

	private static RunResult Failed(int runId, double[] physical, string reason)
	{
		return new RunResult
		{
			RunId = runId,
			Physical = physical,
			Objective = double.PositiveInfinity,
			PerGauge = null,
			Succeeded = false,
			FailureReason = reason,
			ResultDirectory = null,
			Preserved = false
		};
	}
}
