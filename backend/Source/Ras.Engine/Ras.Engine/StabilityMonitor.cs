using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Geospatial.Vectors;
using Utility.NumericalAnalysis;

namespace Ras.Engine;

public sealed class StabilityMonitor
{
	private readonly struct ReplayProbe(StabilityTracker<double> tracker, int realId, double minElev)
	{
		public StabilityTracker<double> Tracker { get; } = tracker;

		public int RealId { get; } = realId;

		public double MinElev { get; } = minElev;
	}

	private readonly Solver _solver;

	private readonly Mesh _mesh;

	private readonly CancellationTokenSource _abortSource;

	private readonly List<Action<MeshTimestep>> _samplers = new List<Action<MeshTimestep>>();

	private readonly List<Action<MeshTimestep>> _computeSamplers = new List<Action<MeshTimestep>>();

	private bool _computeHooked;

	private readonly List<StabilityEvent> _events = new List<StabilityEvent>();

	public IReadOnlyList<StabilityEvent> Events => _events;

	public bool AbortRequested { get; private set; }

	public int ErrorCount
	{
		get
		{
			int ordinal = 0;
			foreach (StabilityEvent @event in _events)
			{
				if (@event.Severity == StabilitySeverity.Error)
				{
					ordinal++;
				}
			}
			return ordinal;
		}
	}

	public int WarningCount => _events.Count - ErrorCount;

	public bool HasErrors => ErrorCount > 0;

	public StabilityMonitor(Solver solver, CancellationTokenSource abortSource = null)
	{
		_solver = solver;
		_mesh = solver.Mesh;
		_abortSource = abortSource;
		solver.AddTimestepDone(OnTimestep);
	}

	private void OnTimestep(MeshTimestep mt)
	{
		for (int i = 0; i < _samplers.Count; i++)
		{
			_samplers[i](mt);
		}
	}

	private void OnComputeStep(MeshTimestep mt)
	{
		for (int i = 0; i < _computeSamplers.Count; i++)
		{
			_computeSamplers[i](mt);
		}
	}

	private void HookCompute()
	{
		if (!_computeHooked)
		{
			_solver.AddComputeStepDone(OnComputeStep);
			_computeHooked = true;
		}
	}

	public void MonitorCell(int cell, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		MonitorCell(cell, $"Cell {cell}", config, abortOnError);
	}

	public int MonitorPoint(Point pt, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		int cell = _mesh.GetCell(pt);
		if (cell >= 0)
		{
			MonitorCell(cell, config, abortOnError);
		}
		return cell;
	}

	private void MonitorCell(int cell, string location, StabilityConfig<double>? config, bool abortOnError)
	{
		StabilityConfig<double> cfg = config ?? StabilityConfig<double>.ForStage();
		StabilityTracker<double> tracker = new StabilityTracker<double>(cfg);
		tracker.Escalated = (StabilityResult<double> r) =>
		{
			Emit(location, "WSE", r, cfg, abortOnError);
		};
		double minElev = _solver.PropertyTables.CellMinElevation(cell);
		_samplers.Add((MeshTimestep mt) =>
		{
			MeshWaterState waterState = mt.WaterState;
			double cellElevation = waterState.CellElevation[cell];
			double depth = cellElevation - minElev;
			double velocity = ((waterState.CellVelocity != null) ? waterState.CellVelocity[cell].Length : 0.0);
			tracker.Add(mt.Time, cellElevation, depth, velocity);
		});
	}

	public void MonitorFace(int face, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		MonitorFace(face, $"Face {face}", config, abortOnError);
	}

	private void MonitorFace(int face, string location, StabilityConfig<double>? config, bool abortOnError)
	{
		StabilityConfig<double> cfg = config ?? StabilityConfig<double>.ForVelocity();
		StabilityTracker<double> tracker = new StabilityTracker<double>(cfg);
		tracker.Escalated = (StabilityResult<double> r) =>
		{
			Emit(location, "Vface", r, cfg, abortOnError);
		};
		_samplers.Add((MeshTimestep mt) =>
		{
			tracker.Add(mt.Time, mt.WaterState.FaceVelocity[face]);
		});
	}

	public void MonitorCells(IEnumerable<int> cells, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		foreach (int cell in cells)
		{
			MonitorCell(cell, config, abortOnError);
		}
	}

	public void MonitorFaces(IEnumerable<int> faces, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		foreach (int face in faces)
		{
			MonitorFace(face, config, abortOnError);
		}
	}

	public void MonitorBoundary(BoundaryCondition bc, string name, StabilityConfig<double>? cellConfig = null, StabilityConfig<double>? faceConfig = null, bool abortOnError = false)
	{
		int[] validElements = ValidElements(bc.Cells, _mesh.CellCount);
		if (validElements.Length != 0)
		{
			MonitorElementsAggregate(validElements, "BC[" + name + "] cells", "WSE", (MeshWaterState ws) => ws.CellElevation, cellConfig ?? StabilityConfig<double>.ForStage(), abortOnError);
		}
		int[] validElementsArray = ValidElements(bc.Faces, _mesh.FaceCount);
		if (validElementsArray.Length != 0)
		{
			MonitorElementsAggregate(validElementsArray, "BC[" + name + "] faces", "Vface", (MeshWaterState ws) => ws.FaceVelocity, faceConfig ?? StabilityConfig<double>.ForVelocity(), abortOnError);
		}
	}

	public void AttachDefault(bool abortOnError = false)
	{
		BoundaryConditionCollection boundaryConditions = _solver.GetBoundaryConditions();
		if (boundaryConditions != null)
		{
			AttachBoundaryList(boundaryConditions.FlowBoundaryConditions, "Flow", abortOnError);
			AttachBoundaryList(boundaryConditions.StageBoundaryConditions, "Stage", abortOnError);
			AttachBoundaryList(boundaryConditions.RatingCurveBoundaryConditions, "RatingCurve", abortOnError);
			AttachBoundaryList(boundaryConditions.NormalDepthBoundaryConditions, "NormalDepth", abortOnError);
			AttachBoundaryList(boundaryConditions.VariableFlowBoundaryConditions, "VariableFlow", abortOnError);
			AttachBoundaryList(boundaryConditions.VariableStageBoundaryConditions, "VariableStage", abortOnError);
			AttachBoundaryList(boundaryConditions.SpatiallyVariableStageVelocityBoundaryConditions, "SpatiallyVariableStageVelocity", abortOnError);
			AttachBoundaryList(boundaryConditions.InternalFlowBoundaryConditions, "InternalFlow", abortOnError);
		}
	}

	private void AttachBoundaryList(List<BoundaryCondition> boundaryConditions, string kind, bool abortOnError)
	{
		for (int i = 0; i < boundaryConditions.Count; i++)
		{
			BoundaryCondition bc = boundaryConditions[i];
			string name = $"{kind}{i}";
			bool abortOnError2 = abortOnError;
			MonitorBoundary(bc, name, null, null, abortOnError2);
		}
	}

	public void MonitorPolyline(Polyline line, string name, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		List<(int, bool)> facesAlongPolyline = _mesh.FacesAlongPolyline(line);
		int[] intValues = new int[facesAlongPolyline.Count];
		for (int i = 0; i < intValues.Length; i++)
		{
			intValues[i] = facesAlongPolyline[i].Item1;
		}
		MonitorElementsAggregate(intValues, "Polyline '" + name + "'", "Vface", (MeshWaterState ws) => ws.FaceVelocity, config ?? StabilityConfig<double>.ForVelocity(), abortOnError);
	}

	public void MonitorCellsAggregate(IReadOnlyList<int> cells, string name, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		MonitorElementsAggregate(ValidElements(cells, _mesh.CellCount), "Cells '" + name + "'", "WSE", (MeshWaterState ws) => ws.CellElevation, config ?? StabilityConfig<double>.ForStage(), abortOnError);
	}

	public void MonitorFacesAggregate(IReadOnlyList<int> faces, string name, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		MonitorElementsAggregate(ValidElements(faces, _mesh.FaceCount), "Faces '" + name + "'", "Vface", (MeshWaterState ws) => ws.FaceVelocity, config ?? StabilityConfig<double>.ForVelocity(), abortOnError);
	}

	public void MonitorRegion(IReadOnlyList<int> cells, string name, StabilityConfig<double>? config = null, bool abortOnError = false)
	{
		MonitorCellsAggregate(cells, name, config, abortOnError);
	}

	public void MonitorElementsAggregate(IReadOnlyList<int> elements, string location, string variable, Func<MeshWaterState, double[]> source, StabilityConfig<double> config, bool abortOnError = false)
	{
		if (elements.Count == 0)
		{
			return;
		}
		int[] ids = new int[elements.Count];
		for (int i = 0; i < ids.Length; i++)
		{
			ids[i] = elements[i];
		}
		AggregateStability<double> agg = new AggregateStability<double>(ids.Length, config);
		double[] buf = new double[ids.Length];
		agg.Escalated = (AggregateStabilityResult<double> r) =>
		{
			EmitRegion(location, variable, ids, r, config, abortOnError);
		};
		_samplers.Add((MeshTimestep mt) =>
		{
			double[] sourceArray = source(mt.WaterState);
			for (int j = 0; j < ids.Length; j++)
			{
				buf[j] = sourceArray[ids[j]];
			}
			agg.Add(mt.Time, buf);
		});
	}

	private int[] ValidElements(IReadOnlyList<int> elements, int count)
	{
		if (elements == null)
		{
			return Array.Empty<int>();
		}
		List<int> intList = new List<int>(elements.Count);
		for (int i = 0; i < elements.Count; i++)
		{
			if ((uint)elements[i] < (uint)count)
			{
				intList.Add(elements[i]);
			}
		}
		return intList.ToArray();
	}

	public IReadOnlyDictionary<int, IReadOnlyList<FieldSample>> MonitorField(Func<MeshWaterState, double[]> source, FieldStabilityConfig<double> cfg, IReadOnlyList<int> ids = null, string variable = "WSE", bool abortOnError = false, IReadOnlyList<int> recordIds = null)
	{
		HookCompute();
		bool isCellWse = variable == "WSE";
		int[] idArr = ids?.ToArray();
		int[] recordDense = null;
		List<FieldSample>[] recordLists = null;
		Dictionary<int, IReadOnlyList<FieldSample>> fieldSampleListByInt = new Dictionary<int, IReadOnlyList<FieldSample>>();
		if (recordIds != null && recordIds.Count > 0)
		{
			recordDense = new int[recordIds.Count];
			recordLists = new List<FieldSample>[recordIds.Count];
			for (int i = 0; i < recordIds.Count; i++)
			{
				int recordIdIndex = recordIds[i];
				int recordIdIndex2 = ((idArr == null) ? recordIdIndex : Array.IndexOf(idArr, recordIdIndex));
				if (recordIdIndex2 < 0)
				{
					throw new ArgumentException($"recordId {recordIdIndex} is not in the scanned set.", "recordIds");
				}
				recordDense[i] = recordIdIndex2;
				recordLists[i] = new List<FieldSample>();
				fieldSampleListByInt[recordIdIndex] = recordLists[i];
			}
		}
		FieldStabilityScanner<double> scanner = null;
		double[] buf = null;
		Dictionary<int, ReplayProbe> replay = new Dictionary<int, ReplayProbe>();
		_computeSamplers.Add((MeshTimestep mt) =>
		{
			double[] sourceArray = source(mt.WaterState);
			if (scanner == null)
			{
				if (idArr == null)
				{
					idArr = new int[sourceArray.Length];
					for (int j = 0; j < idArr.Length; j++)
					{
						idArr[j] = j;
					}
				}
				buf = new double[idArr.Length];
				int[] idCapture = idArr;
				scanner = new FieldStabilityScanner<double>(idArr.Length, cfg);
				scanner.Escalated = (FieldStabilityResult r) =>
				{
					OnFieldEscalated(r, idCapture, isCellWse, variable, replay, abortOnError);
				};
				if (recordDense != null)
				{
					scanner.Record(recordDense, recordLists);
				}
			}
			for (int intervalIndex = 0; intervalIndex < idArr.Length; intervalIndex++)
			{
				buf[intervalIndex] = sourceArray[idArr[intervalIndex]];
			}
			scanner.Add(mt.Time, buf);
			if (replay.Count > 0)
			{
				MeshWaterState waterState = mt.WaterState;
				foreach (ReplayProbe replayProbe in replay.Values)
				{
					if (isCellWse)
					{
						double source2 = sourceArray[replayProbe.RealId];
						double depth = source2 - replayProbe.MinElev;
						double velocity = ((waterState.CellVelocity != null) ? waterState.CellVelocity[replayProbe.RealId].Length : 0.0);
						replayProbe.Tracker.Add(mt.Time, source2, depth, velocity);
					}
					else
					{
						replayProbe.Tracker.Add(mt.Time, sourceArray[replayProbe.RealId]);
					}
				}
			}
		});
		return fieldSampleListByInt;
	}

	private void OnFieldEscalated(FieldStabilityResult r, int[] idArr, bool isCellWse, string variable, Dictionary<int, ReplayProbe> replay, bool abortOnError)
	{
		int element = r.Element;
		if ((uint)element < (uint)idArr.Length && !replay.ContainsKey(element))
		{
			int idArr2 = idArr[element];
			EmitField(idArr2, isCellWse, variable, r, abortOnError);
			StabilityConfig<double> tcfg = (isCellWse ? StabilityConfig<double>.ForStage() : StabilityConfig<double>.ForVelocity());
			StabilityTracker<double> stabilityTracker = new StabilityTracker<double>(tcfg);
			string loc = (isCellWse ? $"Cell {idArr2}" : $"Face {idArr2}");
			stabilityTracker.Escalated = (StabilityResult<double> rr) =>
			{
				Emit(loc + " (confirm)", variable, rr, tcfg, abortOnError);
			};
			double minElev = (isCellWse ? ((double)_solver.PropertyTables.CellMinElevation(idArr2)) : 0.0);
			replay[element] = new ReplayProbe(stabilityTracker, idArr2, minElev);
		}
	}

	private void Emit(string location, string variable, StabilityResult<double> r, StabilityConfig<double> cfg, bool abortOnError)
	{
		StabilitySeverity sev = ((r.State == StabilityState.Error) ? StabilitySeverity.Error : StabilitySeverity.Warning);
		string describe = Describe(r.Kind, cfg, r.OscSteps, r.Delta, r.GrowthRatio);
		string message = $"[{sev.ToString().ToUpperInvariant()}] {location} ({variable}): {describe} at t={r.Time:F2} s, value={r.Value:G5}.";
		Record(sev, r.Kind, location, variable, r.Time, r.Value, message, abortOnError);
	}

	private void EmitRegion(string location, string variable, int[] ids, AggregateStabilityResult<double> r, StabilityConfig<double> cfg, bool abortOnError)
	{
		StabilitySeverity sev = ((r.State == StabilityState.Error) ? StabilitySeverity.Error : StabilitySeverity.Warning);
		int idsIndex = ((r.WorstElement >= 0 && r.WorstElement < ids.Length) ? ids[r.WorstElement] : (-1));
		string describe = Describe(r.Kind, cfg, r.MaxOscSteps, r.MaxDelta, double.NaN);
		string message = $"[{sev.ToString().ToUpperInvariant()}] {location} ({variable}): {describe}; worst element {idsIndex}, {r.OscillatingElements} oscillating at t={r.Time:F2} s, value={r.WorstValue:G5}.";
		Record(sev, r.Kind, $"{location} elem {idsIndex}", variable, r.Time, r.WorstValue, message, abortOnError);
	}

	private void EmitField(int realId, bool isCellWse, string variable, FieldStabilityResult r, bool abortOnError)
	{
		StabilitySeverity sev = ((r.State == StabilityState.Error) ? StabilitySeverity.Error : StabilitySeverity.Warning);
		string message2 = (isCellWse ? $"Cell {realId}" : $"Face {realId}");
		string describeField = DescribeField(r.Kind, r.OscPower, r.Residual);
		string message = $"[{sev.ToString().ToUpperInvariant()}] {message2} ({variable}) field screen: {describeField} at t={r.Time:F2} s, value={r.Value:G5}.";
		Record(sev, r.Kind, message2 + " (screen)", variable, r.Time, r.Value, message, abortOnError);
	}

	private static string DescribeField(InstabilityKind kind, double oscPower, double residual)
	{
		StringBuilder sb = new StringBuilder();
		if ((kind & InstabilityKind.NonFinite) != 0)
		{
			Add("non-finite value (NaN/Inf)");
		}
		if ((kind & InstabilityKind.Oscillation) != 0)
		{
			Add($"sustained 2Δt oscillation (rms {Math.Sqrt(Math.Max(0.0, oscPower)):G4})");
		}
		if ((kind & InstabilityKind.Magnitude) != 0)
		{
			Add($"lag-corrected residual {residual:G4} exceeded tolerance");
		}
		if (sb.Length <= 0)
		{
			return "instability";
		}
		return sb.ToString();
		void Add(string s)
		{
			if (sb.Length > 0)
			{
				sb.Append("; ");
			}
			sb.Append(s);
		}
	}

	private void Record(StabilitySeverity sev, InstabilityKind kind, string location, string variable, double time, double computed, string message, bool abortOnError)
	{
		_events.Add(new StabilityEvent
		{
			Severity = sev,
			Kind = kind,
			Location = location,
			Variable = variable,
			Time = time,
			Value = computed,
			Message = message
		});
		if ((sev == StabilitySeverity.Error) & abortOnError)
		{
			AbortRequested = true;
			_abortSource?.Cancel();
			_solver.Reporter?.RequestCancel();
		}
	}

	private static string Describe(InstabilityKind kind, StabilityConfig<double> cfg, int oscSteps, double delta, double growthRatio)
	{
		StringBuilder sb = new StringBuilder();
		if ((kind & InstabilityKind.NonFinite) != 0)
		{
			Add("non-finite value (NaN/Inf)");
		}
		if ((kind & InstabilityKind.NegativeDepth) != 0)
		{
			Add("negative depth (water below bed)");
		}
		if ((kind & InstabilityKind.Oscillation) != 0)
		{
			Add($"sustained 2Δt oscillation {oscSteps} steps (warn {cfg.OscWarnSteps}/err {cfg.OscErrorSteps})");
		}
		if ((kind & InstabilityKind.Magnitude) != 0)
		{
			Add($"per-step change {delta:G4} exceeded tolerance");
		}
		if ((kind & InstabilityKind.Divergence) != 0)
		{
			Add($"oscillation amplitude growing ×{growthRatio:G3}/cycle");
		}
		if ((kind & InstabilityKind.FroudeExceeded) != 0)
		{
			Add("Froude exceeded cap");
		}
		if (sb.Length <= 0)
		{
			return "instability";
		}
		return sb.ToString();
		void Add(string s)
		{
			if (sb.Length > 0)
			{
				sb.Append("; ");
			}
			sb.Append(s);
		}
	}
}
