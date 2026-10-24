using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Geospatial.Vectors;
using Geospatial.Vectors.Topology;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Logging;

namespace Geospatial.Rendering.Editing;

public class TopologyCompletionContext
{
	public delegate OperationResult CompleteAsConceptualMeshFunc(Topology topology, TopologyBuilderParams buildParams, TopologyBuildDiagnostics diagnostics);

	public delegate void TopologyCompletedEvent(OperationResult result);

	private static readonly ILogger _logger = LogHelper.GetLogger("TopologyCompletionContext");

	private Topology.TopologyTinCache _lastCreatedTin;

	private bool _suspendTopologyComplete;

	private int _updateIterationDepth;

	private OperationResult _latestBuildResult;

	private List<Region> _oldRegionsList = new List<Region>();

	private int _latestBuildResultEditStackVersion;

	private TopologyEditContext _context;

	private EditStack _stack;

	private Func<bool> _isAdding;

	private CompleteAsConceptualMeshFunc _completeFunc;

	private bool _hasInitialized;

	private int _healControlPointsIterationDepth;

	private int _fillEmptyRegionIterationDepth;

	private int _removeDuplicateAndDefaultControlPointsIterationDepth;

	public OperationResult LatestBuildResult => _latestBuildResult;

	public int LatestBuildResultEditStackVersion => _latestBuildResultEditStackVersion;

	public event TopologyCompletedEvent TopologyCompleted;

	public static Func<bool> GetIsAddingForEditStack(EditStack editStack)
	{
		Type[] addingTypes = new Type[2]
		{
			typeof(NewArcOperation),
			typeof(AppendPointOperation)
		};
		return () => editStack.TryPeekUndoStack(out var op) && addingTypes.Contains(op.GetType(), null);
	}

	public TopologyCompletionContext(TopologyEditContext parent, EditStack editStack, Func<bool> isAdding, CompleteAsConceptualMeshFunc completeFunc)
	{
		_context = parent;
		_stack = editStack;
		_isAdding = isAdding;
		_completeFunc = completeFunc;
		parent.CompletionContext = this;
	}

	public bool TryInitialize()
	{
		if (_hasInitialized)
		{
			return true;
		}
		_hasInitialized = true;
		CompleteTopology(EditExecutionMode.Execute);
		return true;
	}

	public bool AttachListenersAndExecute(EditOperation op, bool isSilent = false)
	{
		op.OnApplyingCallback = (Func<bool>)Delegate.Combine(op.OnApplyingCallback, new Func<bool>(TryInitialize));
		op.OnAppliedCallback = (Func<bool>)Delegate.Combine(op.OnAppliedCallback, (Func<bool>)(() =>
		{
			CompleteTopology(EditExecutionMode.Execute);
			return true;
		}));
		op.OnUndoneCallback = (Func<bool>)Delegate.Combine(op.OnUndoneCallback, (Func<bool>)(() =>
		{
			CompleteTopology(EditExecutionMode.Undo);
			return true;
		}));
		op.OnRedoneCallback = (Func<bool>)Delegate.Combine(op.OnRedoneCallback, (Func<bool>)(() =>
		{
			CompleteTopology(EditExecutionMode.Redo);
			return true;
		}));
		return _stack.Execute(op, isSilent);
	}

	private void CompleteTopology(EditExecutionMode mode)
	{
		if (_suspendTopologyComplete)
		{
			return;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		_ = _latestBuildResult;
		OperationResult operationResult;
		try
		{
			TopologyBuildDiagnostics topologyBuildDiagnostics = new TopologyBuildDiagnostics();
			operationResult = _completeFunc?.Invoke(_context.Topology, null, topologyBuildDiagnostics);
			_lastCreatedTin = new Topology.TopologyTinCache
			{
				Tin = topologyBuildDiagnostics.CompletedTin,
				TopologyExtent = topologyBuildDiagnostics.TopologyArcsExtent
			};
			bool condition = false;
			if (operationResult.Messages != null)
			{
				foreach (Message message in operationResult.Messages)
				{
					if (((message.MessageType as Topology.TopologyMessageClass) ?? throw new InvalidOperationException("Topology must use TopologyMessageClass for emitted errors.")).RegionCompletionSeverity == Topology.RegionCompletionSeverity.Fatal)
					{
						condition = true;
						break;
					}
				}
			}
			bool isAdding = _isAdding();
			if (!condition)
			{
				if (!isAdding && mode == EditExecutionMode.Execute)
				{
					_updateIterationDepth++;
					_suspendTopologyComplete = true;
					bool healControlPoints = HealControlPoints();
					bool removeDuplicateAndDefaultControlPoints = RemoveDuplicateAndDefaultControlPoints();
					bool fillEmptyRegionsWithControlPoints = FillEmptyRegionsWithControlPoints();
					_oldRegionsList.Clear();
					_oldRegionsList.AddRange(_context.Topology.CompletedRegions);
					_suspendTopologyComplete = false;
					if (_updateIterationDepth > 3)
					{
						throw new InvalidOperationException("This will infinitely loop. Fix or bounce with error... not sure");
					}
					if (healControlPoints | removeDuplicateAndDefaultControlPoints | fillEmptyRegionsWithControlPoints)
					{
						CompleteTopology(mode);
						_updateIterationDepth--;
						return;
					}
					_updateIterationDepth--;
				}
				else if (!isAdding)
				{
					_oldRegionsList.Clear();
					_oldRegionsList.AddRange(_context.Topology.CompletedRegions);
				}
			}
		}
		catch
		{
			operationResult = new OperationResult(success: false);
			operationResult.AddMessage(Topology.MessageCodes.UnknownElementFailure.Create());
		}
		finally
		{
			stopwatch.Stop();
			_logger.LogTrace("Took {Elapsed} to complete the topology.", stopwatch.Elapsed);
		}
		_latestBuildResult = operationResult;
		_latestBuildResultEditStackVersion = _stack.Version;
		TopologyCompleted?.Invoke(_latestBuildResult);
	}

	private bool HealControlPoints()
	{
		if (_healControlPointsIterationDepth > 2)
		{
			throw new InvalidOperationException("This will infinitely loop. Fix or bounce with error... not sure");
		}
		Queue<Topology.HealCommand> identifyControlPointsToHeal = _context.Topology.IdentifyControlPointsToHeal(_oldRegionsList);
		if (identifyControlPointsToHeal.Count == 0)
		{
			return false;
		}
		_healControlPointsIterationDepth++;
		AttachListenersAndExecute(new HealControlPointsOperation(_context, identifyControlPointsToHeal), isSilent: true);
		_healControlPointsIterationDepth--;
		return true;
	}

	private bool FillEmptyRegionsWithControlPoints()
	{
		if (_fillEmptyRegionIterationDepth > 2)
		{
			throw new InvalidOperationException("This will infinitely loop. Fix or bounce with error... not sure");
		}
		List<int> regionsWithNoControlPoints = _context.Topology.GetRegionsWithNoControlPoints();
		if (regionsWithNoControlPoints.Count == 0)
		{
			return false;
		}
		NewControlPointDef[] newControlPointDefs = new NewControlPointDef[regionsWithNoControlPoints.Count];
		if (regionsWithNoControlPoints.Count < 128)
		{
			for (int i = 0; i < regionsWithNoControlPoints.Count; i++)
			{
				int regionsWithNoControlPoint = regionsWithNoControlPoints[i];
				Point bestRegionControlPoint = _context.Topology.GetBestRegionControlPoint(regionsWithNoControlPoint);
				if (!bestRegionControlPoint.IsNull())
				{
					newControlPointDefs[i] = new NewControlPointDef(bestRegionControlPoint, regionsWithNoControlPoint);
				}
			}
		}
		else
		{
			ThreadLocal<List<NewControlPointDef>> tls = new ThreadLocal<List<NewControlPointDef>>(() => new List<NewControlPointDef>(128), trackAllValues: true);
			Utility.Parallel.SmartForEach(regionsWithNoControlPoints, (Span<int> items, int idk) =>
			{
				List<NewControlPointDef> newControlPointDefsList = tls.Value;
				for (int j = 0; j < items.Length; j++)
				{
					int ordinal = items[j];
					Point bestRegionControlPoint2 = _context.Topology.GetBestRegionControlPoint(ordinal);
					if (!bestRegionControlPoint2.IsNull())
					{
						newControlPointDefsList.Add(new NewControlPointDef(bestRegionControlPoint2, ordinal));
					}
				}
			}, new ParallelOptions(), 128);
			int newControlPointDefCountSum = 0;
			foreach (List<NewControlPointDef> newControlPointDefs2 in tls.Values)
			{
				newControlPointDefs2.CopyTo(newControlPointDefs, newControlPointDefCountSum);
				newControlPointDefCountSum += newControlPointDefs2.Count;
			}
		}
		_fillEmptyRegionIterationDepth++;
		AttachListenersAndExecute(new NewControlPointOperation(_context, newControlPointDefs), isSilent: true);
		_fillEmptyRegionIterationDepth--;
		return true;
	}

	public bool RemoveDuplicateAndDefaultControlPoints()
	{
		if (_removeDuplicateAndDefaultControlPointsIterationDepth > 2)
		{
			throw new InvalidOperationException("This will infinitely loop. Fix or bounce with error... not sure");
		}
		IList<int> defaultOrDuplicateControlPoints = _context.Topology.GetDefaultOrDuplicateControlPoints();
		if (defaultOrDuplicateControlPoints.Count == 0)
		{
			return false;
		}
		_removeDuplicateAndDefaultControlPointsIterationDepth++;
		AttachListenersAndExecute(new DeleteControlPointsOperation(_context, defaultOrDuplicateControlPoints), isSilent: true);
		_removeDuplicateAndDefaultControlPointsIterationDepth--;
		return true;
	}
}
