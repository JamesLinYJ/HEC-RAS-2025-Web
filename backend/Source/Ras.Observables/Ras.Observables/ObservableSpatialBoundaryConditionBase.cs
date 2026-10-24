using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Layers;
using Ras.Layers.BoundaryConditions;
using Utility.Validation;

namespace Ras.Observables;

public abstract class ObservableSpatialBoundaryConditionBase : ObservableValidator
{
	public abstract Type[] ValidUnitTypes { get; }

	public abstract bool IsEnabled { get; set; }

	public abstract float Ratio { get; set; }

	public abstract MetInterpolationType InterpolationMethod { get; set; }

	public virtual SpatialBoundaryConditionBase Layer { get; }

	public event EventHandler ImportedDataChanged;

	protected virtual void OnImportedDataChangedChanged()
	{
		ImportedDataChanged?.Invoke(this, EventArgs.Empty);
	}

	public void AlertImportedRasterDataChanged()
	{
		OnImportedDataChangedChanged();
	}
}
public abstract class ObservableSpatialBoundaryConditionBase<T> : ObservableSpatialBoundaryConditionBase where T : SpatialBoundaryConditionBase
{
	public delegate void SpatialDataTypeChangedEvent(SpatialDataType oldValue, SpatialDataType newValue);

	protected readonly T _layer;

	private ObservableBoundaryCondition _bc;

	public override T Layer => _layer;

	public ObservableBoundaryCondition BoundaryCondition
	{
		get
		{
			return _bc;
		}
		set
		{
			SetProperty(ref _bc, value, "BoundaryCondition");
		}
	}

	public SpatialDataType SpatialDataType
	{
		get
		{
			return _layer.SpatialDataType;
		}
		set
		{
			if (_layer.SpatialDataType != value)
			{
				SpatialDataType spatialDataType = _layer.SpatialDataType;
				if (SetProperty(_layer.SpatialDataType, value, _layer, (T layer, SpatialDataType newval) =>
				{
					layer.SpatialDataType = newval;
				}, "SpatialDataType"))
				{
					SpatialDataTypeChanged?.Invoke(spatialDataType, value);
				}
			}
		}
	}

	[RecommendedBounds(0.0, double.MaxValue, null, false)]
	public override float Ratio
	{
		get
		{
			return _layer.Ratio;
		}
		set
		{
			SetProperty(_layer.Ratio, value, _layer, (T layer, float newval) =>
			{
				layer.Ratio = newval;
			}, "Ratio");
		}
	}

	public override bool IsEnabled
	{
		get
		{
			return _layer.IsEnabled;
		}
		set
		{
			SetProperty(_layer.IsEnabled, value, _layer, (T layer, bool newval) =>
			{
				layer.IsEnabled = newval;
			}, "IsEnabled");
		}
	}

	public override MetInterpolationType InterpolationMethod
	{
		get
		{
			return _layer.InterpolationMethod;
		}
		set
		{
			SetProperty(_layer.InterpolationMethod, value, _layer, (T layer, MetInterpolationType newval) =>
			{
				layer.InterpolationMethod = newval;
			}, "InterpolationMethod");
		}
	}

	public event SpatialDataTypeChangedEvent SpatialDataTypeChanged;

	public ObservableSpatialBoundaryConditionBase(ObservableBoundaryCondition bc, T layer)
	{
		_bc = bc;
		_layer = layer;
	}
}
