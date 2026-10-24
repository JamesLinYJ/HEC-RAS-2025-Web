using System;
using Ras.Layers;

namespace Ras.Observables;

public class ObservableBoundaryCondition : ObservableCoreLayer
{
	public delegate void DateTimeChangedEvent(ObservableBoundaryCondition bc, DateTime newTime);

	public static Type Type => typeof(ObservableBoundaryCondition);

	public new BoundaryCondition Layer => base.Layer as BoundaryCondition;

	public BoundaryCondition BoundaryCondition => Layer;

	public DateTime StartTime
	{
		get
		{
			return BoundaryCondition.StartTime;
		}
		set
		{
			SetProperty(BoundaryCondition.StartTime, value, BoundaryCondition, (BoundaryCondition a, DateTime b) =>
			{
				a.StartTime = b;
			}, "StartTime");
			InvalidateChildrenStartTimes();
			StartTimeChanged?.Invoke(this, value);
		}
	}

	public DateTime EndTime
	{
		get
		{
			return BoundaryCondition.EndTime;
		}
		set
		{
			SetProperty(BoundaryCondition.EndTime, value, BoundaryCondition, (BoundaryCondition a, DateTime b) =>
			{
				a.EndTime = b;
			}, "EndTime");
			InvalidateChildrenEndTimes();
			EndTimeChanged?.Invoke(this, value);
		}
	}

	public TimeSpan WarmupTime
	{
		get
		{
			return BoundaryCondition.WarmupTime;
		}
		set
		{
			SetProperty(BoundaryCondition.WarmupTime, value, BoundaryCondition, (BoundaryCondition a, TimeSpan b) =>
			{
				a.WarmupTime = b;
			}, "WarmupTime");
		}
	}

	public bool IsResult => BoundaryCondition.IsResult;

	public ObservableResult Result { get; }

	public ObservablePrecipitationLayer PrecipitationLayer { get; }

	public ObservableEvapotranspirationLayer EvapotranspirationLayer { get; }

	public ObservableWindLayer WindLayer { get; }

	public ObservableAirDensityLayer AirDensity { get; }

	public ObservableAirPressureLayer AirPressure { get; }

	public ObservableBoundaryConditionLineLayer BCLineLayer { get; }

	public event DateTimeChangedEvent StartTimeChanged;

	public event DateTimeChangedEvent EndTimeChanged;

	public ObservableBoundaryCondition(ObservableProject project, BoundaryCondition bc)
		: base(project, bc)
	{
		EvapotranspirationLayer = new ObservableEvapotranspirationLayer(this);
		PrecipitationLayer = new ObservablePrecipitationLayer(this);
		WindLayer = new ObservableWindLayer(this);
		AirDensity = new ObservableAirDensityLayer(this);
		AirPressure = new ObservableAirPressureLayer(this);
		BCLineLayer = new ObservableBoundaryConditionLineLayer(this);
	}

	public ObservableBoundaryCondition(ObservableResult parent)
		: base(parent.Project, parent.Result.BoundaryConditions)
	{
		Result = parent;
		EvapotranspirationLayer = new ObservableEvapotranspirationLayer(this);
		PrecipitationLayer = new ObservablePrecipitationLayer(this);
		WindLayer = new ObservableWindLayer(this);
		AirDensity = new ObservableAirDensityLayer(this);
		AirPressure = new ObservableAirPressureLayer(this);
		BCLineLayer = new ObservableBoundaryConditionLineLayer(this);
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemoveBoundaryCondition(this);
		Layer.DisposeAndDeleteAllBackingFiles();
	}

	public override bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText)
	{
		if (!TryRenameByDeleteAndSaveAsH5Internal(inputName, BoundaryCondition.MakeFilename, out errorText))
		{
			return false;
		}
		foreach (ObservablePlan plan in Project.Plans)
		{
			if (plan.AssociatedBoundaryCondition == this)
			{
				plan.Plan.PlanAssociation.AlertBoundaryConditionRenamed(inputName);
			}
		}
		return true;
	}

	private void InvalidateChildrenStartTimes()
	{
		if (BCLineLayer == null)
		{
			return;
		}
		foreach (ObservableBoundaryConditionLine bcLineLayer in BCLineLayer)
		{
			if (!bcLineLayer.AnyPayloadsUseFixedStartTime)
			{
				bcLineLayer.InvalidateTime();
			}
		}
	}

	private void InvalidateChildrenEndTimes()
	{
		if (BCLineLayer == null)
		{
			return;
		}
		foreach (ObservableBoundaryConditionLine bcLineLayer in BCLineLayer)
		{
			bcLineLayer.InvalidateTime();
		}
	}
}
