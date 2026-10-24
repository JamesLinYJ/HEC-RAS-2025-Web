using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors;
using Microsoft.Extensions.Logging;
using Ras.Layers.BoundaryConditions;
using Ras.Layers.Payloads;
using Utility;
using Utility.DataStructures;
using Utility.Filtering;
using Utility.Logging;
using Utility.Validation;
using Visual.Mapping.Observables.TimeSeries;
using Visual.Observables.Filtering;

namespace Ras.Observables;

public class ObservableBoundaryConditionLine : ObservableValidator, IObservableFeature<BoundaryConditionLine>
{
	public delegate void SelectedPayloadChangedEvent(ObservableBoundaryConditionLine sender, ObservablePayload payload);

	public delegate void BoundaryConditionTimingChangedEvent(ObservableBoundaryConditionLine sender);

	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableBoundaryConditionLine");

	public const int MIN_TABLE_ENTRIES_REQUIRED = 100;

	private readonly BoundaryConditionLine _bcLine;

	private readonly object _payloadsLoadLock = new object();

	private AdvancedCollection<ObservablePayload> _payloads;

	private readonly object _payloadsWithContextLoadLock = new object();

	private ReadOnlyMappedAdvancedCollection<ObservablePayload, ObservablePayloadWithContextWrapper> _payloadsWithContext;

	private readonly object _hydraulicPayloadsWithContextLoadLock = new object();

	private ObservableFilterResults<ObservablePayloadWithContextWrapper> _hydraulicPayloadsWithContext;
	private ObservablePayloadWithContextWrapper _selectedPayload;

	public BoundaryConditionLine BaseFeature => _bcLine;

	public ObservableBoundaryCondition BoundaryCondition { get; private set; }

	public Polyline Polyline
	{
		get
		{
			return _bcLine.Polyline;
		}
		set
		{
			SetProperty(_bcLine.Polyline, value, _bcLine, (BoundaryConditionLine r, Polyline v) =>
			{
				r.Polyline = v;
			}, "Polyline");
		}
	}

	[CustomValidation(typeof(ObservableBoundaryConditionLine), "ValidateUniqueName")]
	public string Name
	{
		get
		{
			return _bcLine.Name;
		}
		set
		{
			SetProperty(_bcLine.Name, value, _bcLine, (BoundaryConditionLine r, string v) =>
			{
				r.Name = v;
			}, "Name");
			ValidateProperty(value, "Name");
		}
	}

	public IReadOnlyAdvancedCollection<ObservablePayload> Payloads => LazyThreadsafe<AdvancedCollection<ObservablePayload>>.AllocateOnce(ref _payloads, _payloadsLoadLock, this, BuildObservablePayloads);

	public IReadOnlyAdvancedCollection<ObservablePayloadWithContextWrapper> PayloadsWithContext => LazyThreadsafe<ReadOnlyMappedAdvancedCollection<ObservablePayload, ObservablePayloadWithContextWrapper>>.AllocateOnce(ref _payloadsWithContext, _payloadsWithContextLoadLock, this, BuildObservablePayloadsWithContext);

	public IReadOnlyAdvancedCollection<ObservablePayloadWithContextWrapper> HydraulicPayloadsWithContext => LazyThreadsafe<ObservableFilterResults<ObservablePayloadWithContextWrapper>>.AllocateOnce(ref _hydraulicPayloadsWithContext, _hydraulicPayloadsWithContextLoadLock, BuildHydraulicPayloadsWithContext).FilteredItemsSource;

	public DateTime ActualStartTime
	{
		get
		{
			if (_bcLine.Payloads.Count != 0)
			{
				return _bcLine.Payloads.Min((PayloadBase pl) => (pl as ITimeSeriesPayload)?.TimeSeriesDefinition.StartTime(BoundaryCondition.StartTime) ?? DateTime.MaxValue);
			}
			return DateTime.MinValue;
		}
	}

	public DateTime ActualEndTime
	{
		get
		{
			if (_bcLine.Payloads.Count != 0)
			{
				return _bcLine.Payloads.Max((PayloadBase pl) => pl switch
				{
					TimeSeriesPayload series when series.Count > 0 => series.TimeSeriesDefinition.TimeFor(BoundaryCondition.StartTime, series.Count - 1),
					SpatiallyVariableTimeSeriesPayload spatial when spatial.Values.Count > 0 => spatial.TimeSeriesDefinition.TimeFor(BoundaryCondition.StartTime, spatial.Values.Count - 1),
					_ => DateTime.MinValue
				});
			}
			return DateTime.MinValue;
		}
	}

	public bool AnyPayloadsUseFixedStartTime => Payloads.Any((ObservablePayload pl) => pl is ObservableTimeSeriesPayload observableTimeSeriesPayload && (observableTimeSeriesPayload.TimeSeriesDefinition is ObservableRegularTimeSeriesDefinition { UseFixedStartTime: not false } || observableTimeSeriesPayload.TimeSeriesDefinition is ObservableIrregularAbsoluteTimeSeriesDefinition));

	public ObservablePayloadWithContextWrapper ActiveHydraulicPayload
	{
		get
		{
			IReadOnlyAdvancedCollection<ObservablePayloadWithContextWrapper> hydraulicPayloadsWithContext = HydraulicPayloadsWithContext;
			if (!hydraulicPayloadsWithContext.Any())
			{
				return null;
			}
			if (SelectedPayload != null && hydraulicPayloadsWithContext.Contains(SelectedPayload) && SelectedPayload.Payload.IsEnabled)
			{
				return SelectedPayload;
			}
			IEnumerable<ObservablePayloadWithContextWrapper> source = hydraulicPayloadsWithContext.Where((ObservablePayloadWithContextWrapper pl) => pl.Payload.IsEnabled);
			if (!source.Any())
			{
				return null;
			}
			return SelectedPayload = source.First();
		}
		set
		{
			if (value == null)
			{
				throw new InvalidOperationException("Can't set active payload to null.");
			}
			if (value != ActiveHydraulicPayload)
			{
				if (!HydraulicPayloadsWithContext.Contains(value))
				{
					throw new InvalidOperationException("Can't set active payload to non-hydraulic payload.");
				}
				value.Payload.IsEnabled = true;
				SelectedPayload = value;
				OnPropertyChanged("ActiveHydraulicPayload");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservablePayloadWithContextWrapper SelectedPayload
	{
		get
		{
			return _selectedPayload;
		}
		[MemberNotNull("_selectedPayload")]
		set
		{
			if (!EqualityComparer<ObservablePayloadWithContextWrapper>.Default.Equals(_selectedPayload, value))
			{
				ObservablePayloadWithContextWrapper selectedPayload = _selectedPayload;
				OnPropertyChanging("SelectedPayload");
				_selectedPayload = value;
				OnSelectedPayloadChanged(selectedPayload, value);
				OnPropertyChanged("SelectedPayload");
			}
		}
	}

	public event SelectedPayloadChangedEvent SelectedPayloadChanged;

	public event BoundaryConditionTimingChangedEvent BoundaryConditionTimingChanged;

	private static AdvancedCollection<ObservablePayload> BuildObservablePayloads(ObservableBoundaryConditionLine obsBCL)
	{
		AdvancedCollection<ObservablePayload> advancedCollection = new AdvancedCollection<ObservablePayload>();
		BoundaryConditionLine bcLine = obsBCL._bcLine;
		bool condition = false;
		for (int i = 0; i < bcLine.Payloads.Count; i++)
		{
			PayloadBase payloadBase = bcLine.Payloads[i];
			if (WellKnownPayloadVariables.IsHydraulicPayload(payloadBase.Variable))
			{
				if (condition)
				{
					payloadBase.IsEnabled = false;
				}
				else if (payloadBase.IsEnabled)
				{
					condition = true;
				}
			}
			ObservablePayload observablePayload = ObservablePayload.From(obsBCL.BoundaryCondition.Project, payloadBase);
			obsBCL.InitializePayload(observablePayload);
			advancedCollection.Add(observablePayload);
		}
		if (!condition)
		{
			foreach (ObservablePayload advancedCollection2 in advancedCollection)
			{
				if (WellKnownPayloadVariables.IsHydraulicPayload(advancedCollection2.Variable))
				{
					advancedCollection2.IsEnabled = true;
					break;
				}
			}
		}
		return advancedCollection;
	}

	private static ReadOnlyMappedAdvancedCollection<ObservablePayload, ObservablePayloadWithContextWrapper> BuildObservablePayloadsWithContext(ObservableBoundaryConditionLine obsBCL)
	{
		return new ReadOnlyMappedAdvancedCollection<ObservablePayload, ObservablePayloadWithContextWrapper>(obsBCL.Payloads, (ObservablePayload obsPayload) => new ObservablePayloadWithContextWrapper(obsPayload, obsBCL));
	}

	public ObservableFilterResults<ObservablePayloadWithContextWrapper> BuildHydraulicPayloadsWithContext()
	{
		FunctionalFilter<ObservablePayloadWithContextWrapper> functionalFilter = new FunctionalFilter<ObservablePayloadWithContextWrapper>((ObservablePayloadWithContextWrapper i) => WellKnownPayloadVariables.IsHydraulicPayload(i.Payload.Variable));
		return new ObservableFilterResults<ObservablePayloadWithContextWrapper>(PayloadsWithContext, functionalFilter);
	}

	public ObservableBoundaryConditionLine(ObservableBoundaryCondition bc, BoundaryConditionLine bcLine)
	{
		BoundaryCondition = bc;
		_bcLine = bcLine;
	}

	public static ValidationResult ValidateUniqueName(string newName, ValidationContext ctx)
	{
		if (!(ctx.ObjectInstance is ObservableBoundaryConditionLine observableBoundaryConditionLine))
		{
			return new ValidationResult("Error initializing ObserableBoundaryConditionLine");
		}
		foreach (ObservableBoundaryConditionLine feature in observableBoundaryConditionLine.BoundaryCondition.BCLineLayer.Features)
		{
			if (!feature.Equals(observableBoundaryConditionLine) && string.Equals(feature.Name, newName, StringComparison.OrdinalIgnoreCase))
			{
				return new LeveledValidationResult(newName + " matches an existing boundary condition line's name.", ValidationErrorLevel.Warn);
			}
		}
		return LeveledValidationResult.Success;
	}

	public void AddPayload(ObservablePayload payload)
	{
		AdvancedCollection<ObservablePayload> advancedCollection = (AdvancedCollection<ObservablePayload>)Payloads;
		InitializePayload(payload);
		if (payload.IsEnabled)
		{
			TryDisableOtherHydraulicPayloads(payload);
		}
		advancedCollection.Add(payload);
		_bcLine.Add(payload.Payload);
		OnPropertyChanged("ActiveHydraulicPayload");
	}

	public void RemovePayload(ObservablePayload payload)
	{
		AdvancedCollection<ObservablePayload> advancedCollection = (AdvancedCollection<ObservablePayload>)Payloads;
		DeinitializePayload(payload);
		if (SelectedPayload != null && SelectedPayload.Payload == payload)
		{
			SelectedPayload = null;
		}
		advancedCollection.Remove(payload);
		_bcLine.Remove(payload.Payload);
		OnPropertyChanged("ActiveHydraulicPayload");
	}

	private void InitializePayload(ObservablePayload payload)
	{
		payload.IsEnabledChanged += PayloadIsEnabledChanged;
	}

	private void DeinitializePayload(ObservablePayload payload)
	{
		payload.IsEnabledChanged -= PayloadIsEnabledChanged;
	}

	private void PayloadIsEnabledChanged(ObservablePayload sender, bool isEnabled)
	{
		if (isEnabled)
		{
			TryDisableOtherHydraulicPayloads(sender);
			OnPropertyChanged("ActiveHydraulicPayload");
		}
	}

	private void TryDisableOtherHydraulicPayloads(ObservablePayload sender)
	{
		TryDisableOtherHydraulicPayloads(HydraulicPayloadsWithContext, sender);
	}

	private static void TryDisableOtherHydraulicPayloads(IEnumerable<ObservablePayloadWithContextWrapper> payloads, ObservablePayload sender)
	{
		if (!WellKnownPayloadVariables.IsHydraulicPayload(sender.Payload))
		{
			return;
		}
		foreach (ObservablePayloadWithContextWrapper payload in payloads)
		{
			if (!payload.Payload.Equals(sender) && payload.Payload.IsEnabled)
			{
				payload.Payload.IsEnabled = false;
			}
		}
	}

	private void FixedStateTimeChanged(ObservableTimeSeriesDefinition sender, DateTime dateTime)
	{
		InvalidateTime();
	}

	private void RTSTimeSpanChanged(ObservableTimeSeriesDefinition sender, TimeSpan timeSpan)
	{
		InvalidateTime();
	}

	public void InvalidateTime()
	{
		OnPropertyChanged("ActualStartTime");
		OnPropertyChanged("ActualEndTime");
		BoundaryConditionTimingChanged?.Invoke(this);
	}

	public override string ToString()
	{
		return Name;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedPayloadChanged(ObservablePayloadWithContextWrapper? oldValue, ObservablePayloadWithContextWrapper newValue)
	{
		SelectedPayloadChanged?.Invoke(this, newValue?.Payload);
	}
}
