using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Geospatial;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Ras.Layers.Payloads;
using Utility;
using Utility.Extensions;
using Utility.Files;
using Utility.Hashing;
using Utility.Logging;

namespace Ras.Layers.BoundaryConditions;

[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public class BoundaryConditionLine : IFeatureRow, ITableRow, ISpatialVersion, IDataHash, IExtent
{
	private static readonly ILogger _logger = LogHelper.GetLogger("BoundaryConditionLine");

	public const string DefaultName = "Boundary Condition Line";

	private Polyline _polyline;

	private VersionTracker _tracker;

	private BoundaryCondition _boundaryCondition;

	[HashContributor]
	public Polyline Polyline
	{
		get
		{
			return _polyline;
		}
		set
		{
			_tracker.Assign(ref _polyline, value);
		}
	}

	public Extent Extent => Polyline?.Extent ?? Extent.NullExtent;

	public long VersionXY => _tracker.GetVersion(_polyline);

	[HashContributor]
	public string Name { get; set; }

	[HashContributor]
	public string Description { get; set; }

	[HashContributor]
	public bool IsEnabled { get; set; }

	public BoundaryFlowDistributionMethod FlowDistributionMethod { get; set; }

	[HashContributor]
	public List<PayloadBase> Payloads { get; set; }

	public BoundaryCondition BoundaryCondition
	{
		get
		{
			return _boundaryCondition;
		}
		set
		{
			if (_boundaryCondition != null)
			{
				if (value != _boundaryCondition)
				{
					throw new InvalidOperationException("Parent boundary condition already initialized.");
				}
				_logger.LogWarning("BC Line double-load assigning parent BC.");
			}
			else
			{
				_boundaryCondition = value;
			}
		}
	}

	public BoundaryConditionLine()
	{
		Init();
	}

	public BoundaryConditionLine(BoundaryCondition bc)
	{
		BoundaryCondition = bc;
		Init();
	}

	public BoundaryConditionLine(BoundaryCondition bc, Polyline pl)
	{
		BoundaryCondition = bc;
		_polyline = pl;
		Init();
	}

	public void Add(PayloadBase pl)
	{
		Payloads.Add(pl);
	}

	public void Remove(PayloadBase pl)
	{
		Payloads.Remove(pl);
	}

	public void AddExternal(string variable, string externalConnection, string externalPath = "")
	{
		Payloads.Add(new ExternalPayload(variable, externalConnection, externalPath));
	}

	public static BoundaryConditionLine ConstantStage(BoundaryCondition bc, Polyline pl, double doubleValue)
	{
		ConstantPayload pl2 = new ConstantPayload("Stage", doubleValue);
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl);
		boundaryConditionLine.Add(pl2);
		return boundaryConditionLine;
	}

	public static BoundaryConditionLine ConstantFlow(BoundaryCondition bc, Polyline pl, double doubleValue)
	{
		ConstantPayload pl2 = new ConstantPayload("Flow", doubleValue);
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl);
		boundaryConditionLine.Add(pl2);
		return boundaryConditionLine;
	}

	public static BoundaryConditionLine SpatiallyVariableStageVelocity(BoundaryCondition bc, Polyline pl, SpatiallyVariableTimeSeriesPayload payload)
	{
		if (payload == null)
		{
			throw new ArgumentNullException("payload");
		}
		if (!payload.Variable.RoughlyEquals("StageVelocityVelocity"))
		{
			throw new ArgumentException("Payload variable must be StageVelocityVelocity.", "payload");
		}
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl);
		boundaryConditionLine.Add(payload);
		return boundaryConditionLine;
	}

	public static BoundaryConditionLine SpatiallyVariableStage(BoundaryCondition bc, Polyline pl, SpatiallyVariableTimeSeriesPayload payload)
	{
		if (payload == null)
		{
			throw new ArgumentNullException("payload");
		}
		if (!payload.Variable.RoughlyEquals("Stage"))
		{
			throw new ArgumentException("Payload variable must be Stage.", "payload");
		}
		int stationCount = payload.StationCount;
		for (int i = 0; i < payload.Values.Count; i++)
		{
			if (payload.Values[i].Length != stationCount)
			{
				throw new ArgumentException($"Timestep row {i} has {payload.Values[i].Length} values, expected {stationCount} (one per station).", "payload");
			}
		}
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl);
		boundaryConditionLine.Add(payload);
		return boundaryConditionLine;
	}

	public static BoundaryConditionLine NormalDepth(BoundaryCondition bc, Polyline pl, double doubleValue)
	{
		ConstantPayload pl2 = new ConstantPayload("Normal Depth", doubleValue);
		BoundaryConditionLine boundaryConditionLine = new BoundaryConditionLine(bc, pl);
		boundaryConditionLine.Add(pl2);
		return boundaryConditionLine;
	}

	public void InitializeBoundaryCondition(BoundaryCondition bc)
	{
		if (BoundaryCondition != null)
		{
			if (BoundaryCondition != bc)
			{
				throw new InvalidOperationException("Boundary condition line parent alreadey initialized.");
			}
			_logger.LogWarning("Double initialized boundary condition line ({bc}) parent ({bcPar})", Name, bc.Name);
		}
		BoundaryCondition = bc;
	}

	private void Init()
	{
		Payloads = new List<PayloadBase>();
		IsEnabled = true;
	}

	public bool HasEnabledHydraulicPayload()
	{
		return Payloads.Any((PayloadBase pl) => pl.IsEnabled && WellKnownPayloadVariables.IsHydraulicPayload(pl));
	}

	public OperationResult TryGetHydraulicPayload(out PayloadBase pl)
	{
		List<PayloadBase> payloadBases = Payloads.Where((PayloadBase payloadBase) => payloadBase.IsEnabled && WellKnownPayloadVariables.IsHydraulicPayload(payloadBase)).ToList();
		if (payloadBases.Count == 0)
		{
			pl = null;
			return OperationResult.Fail("No hydraulic payloads found on Boundary Condition Line " + Name + ".");
		}
		if (payloadBases.Count == 1)
		{
			pl = payloadBases[0];
			return true;
		}
		pl = null;
		return OperationResult.Fail("Multiple active hydraulic payloads found on Boundary Condition Line " + Name + ". Only one may be enabled to drive the model.");
	}

	public OperationResult TrySetHydraulicPayload(string payloadName)
	{
		PayloadBase payloadBase = Payloads.FirstOrDefault((PayloadBase p) => p.Name == payloadName);
		if (payloadBase == null)
		{
			return OperationResult.Fail(new Message("No payload found with the name {PayloadName}.", payloadName));
		}
		if (WellKnownPayloadVariables.IsHydraulicPayload(payloadBase))
		{
			payloadBase.IsEnabled = true;
			foreach (PayloadBase payload in Payloads)
			{
				if (payload != payloadBase && WellKnownPayloadVariables.IsHydraulicPayload(payload))
				{
					payload.IsEnabled = false;
				}
			}
			return true;
		}
		return OperationResult.Fail(new Message("Payload found with name {PayloadName}, but it is not a hydraulic payload (Variable = {VariableType}).", payloadName, payloadBase.Variable));
	}

	public void SetHydraulicPayload(PayloadBase payload)
	{
		if (!WellKnownPayloadVariables.IsHydraulicPayload(payload))
		{
			throw new Exception("Given Payload is not a hydraulic type.");
		}
		if (!Payloads.Contains(payload))
		{
			Payloads.Add(payload);
		}
		payload.IsEnabled = true;
		foreach (PayloadBase payload2 in Payloads)
		{
			if (payload != payload2 && WellKnownPayloadVariables.IsHydraulicPayload(payload2))
			{
				payload2.IsEnabled = false;
			}
		}
	}

	public OperationResult CoversTimeWindow(DateTime startTime, TimeSpan duration)
	{
		if (!HasEnabledHydraulicPayload())
		{
			return OperationResult.Success();
		}
		OperationResult operationResult = TryGetHydraulicPayload(out var pl);
		if (!operationResult)
		{
			return operationResult;
		}
		ExternalConnectionCollection connectionSource = BoundaryCondition.Project.ConnectionSource;
		return pl.CoversTimeWindow(connectionSource, Polyline, startTime, duration, BoundaryCondition.StartTime);
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	private string GetDebuggerDisplay()
	{
		if (!TryGetHydraulicPayload(out var pl))
		{
			return Name;
		}
		return Name + " " + pl.Variable.InParentheses();
	}

	public OperationResult DataEquals(BoundaryConditionLine other)
	{
		OperationResult operationResult = true;
		if (GetDataHash() != other.GetDataHash())
		{
			operationResult.AddFailure("BCL hash mismatch.");
		}
		if (!Polyline.EqualsXY(other.Polyline))
		{
			operationResult.AddFailure("Polyline mismatch.");
		}
		if (!StringHelpers.StringEquals(Name, other.Name))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryConditionLine", "Name", Name, other.Name));
		}
		if (!StringHelpers.StringEquals(Description, other.Description))
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryConditionLine", "Description", Description, other.Description));
		}
		if (IsEnabled != other.IsEnabled)
		{
			operationResult.AddFailure(GeneralMessageCodes.GetPropertyValueMismatch("BoundaryConditionLine", "IsEnabled", IsEnabled, other.IsEnabled));
		}
		if (Payloads.Count != other.Payloads.Count)
		{
			operationResult.AddFailure("Payload count mismatch.");
		}
		else
		{
			for (int i = 0; i < Payloads.Count; i++)
			{
				PayloadBase payloadBase = Payloads[i];
				PayloadBase other2 = other.Payloads[i];
				if (!payloadBase.DataEquals(other2))
				{
					operationResult.AddFailure(new Message("Payload[{PayloadIndex}] mismatch.", i));
				}
			}
		}
		return operationResult;
	}
}
