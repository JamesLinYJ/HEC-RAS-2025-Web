using System;
using System.ComponentModel;
using System.Threading;
using Ras.Layers.Payloads;
using Utility.Helpers;
using Visual.Mapping.Observables.TimeSeries;

namespace Ras.Observables;

public class ObservableTimeSeriesPayload : ObservablePayload, IObservableTimeSeriesContainer, INotifyPropertyChanged
{
	private readonly Lock _loadLock = new Lock();

	private BindableMemoryWrapper<double> _values;

	private ObservableTimeSeriesDefinitionBase _timeSeriesDefinition;

	public static Type Type => typeof(ObservableTimeSeriesPayload);

	public override TimeSeriesPayload Payload => _payload as TimeSeriesPayload;

	public BindableMemoryWrapper<double> Values
	{
		get
		{
			if (_values != null)
			{
				return _values;
			}
			using (_loadLock.EnterScope())
			{
				if (_values == null)
				{
					BindableMemoryWrapper<double> bindableMemoryWrapper = new BindableMemoryWrapper<double>(Payload.Values, (BindableMemoryWrapper<double>.SetValueArgs args) =>
					{
						Payload.Values = args.NewValues;
						return Payload.Values;
					}, double.NaN, isFixedSize: false);
					SetProperty(ref _values, bindableMemoryWrapper, "Values");
					bindableMemoryWrapper.PropertyChanged += Wrapper_PropertyChanged;
				}
			}
			return _values;
		}
	}

	public int Count => Payload.Count;

	public ObservableTimeSeriesDefinitionBase TimeSeriesDefinition
	{
		get
		{
			if (_timeSeriesDefinition == null)
			{
				_timeSeriesDefinition = ObservableTimeSeriesDefinitionBase.From(Payload.TimeSeriesDefinition);
			}
			if (_timeSeriesDefinition.Definition != Payload.TimeSeriesDefinition)
			{
				throw new InvalidOperationException("Cached TimeSeriesDefinitionBase does not equal payload's. Model value set without being chained through observable.");
			}
			return _timeSeriesDefinition;
		}
	}

	public override string FriendlyName
	{
		get
		{
			ObservableTimeSeriesDefinitionBase timeSeriesDefinition = TimeSeriesDefinition;
			if (!(timeSeriesDefinition is ObservableRegularTimeSeriesDefinition))
			{
				if (!(timeSeriesDefinition is ObservableIrregularAbsoluteTimeSeriesDefinition))
				{
					if (timeSeriesDefinition is ObservableIrregularRelativeTimeSeriesDefinition)
					{
						return "Irregular Relative Time Series";
					}
					throw new NotImplementedException();
				}
				return "Irregular Absolute Time Series";
			}
			return "Regular Time Series";
		}
	}

	private void Wrapper_PropertyChanged(object sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName.Equals("Count"))
		{
			OnPropertyChanged("Count");
		}
	}

	public ObservableTimeSeriesPayload(TimeSeriesPayload payload)
		: base(payload)
	{
	}

	public void InterpolateData()
	{
		ListHelpers.LinearInterpolateNaNsInPlace(Values, Values.Count, (BindableMemoryWrapper<double> m, int idx) => m[idx], (BindableMemoryWrapper<double> m, int idx, double computed) =>
		{
			m[idx] = computed;
		});
	}

	public void Clear()
	{
		for (int i = 0; i < Values.Count; i++)
		{
			Values[i] = double.NaN;
		}
	}
}
