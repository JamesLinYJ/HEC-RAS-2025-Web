using System;
using System.ComponentModel;
using Ras.Layers.Payloads;
using Utility.DataStructures;
using Utility.Helpers;
using Visual.Mapping.Observables.TimeSeries;

namespace Ras.Observables;

public class ObservableSpatiallyVariableTimeSeriesPayload : ObservablePayload, IObservableTimeSeriesContainer, INotifyPropertyChanged
{
	private ObservableTimeSeriesDefinitionBase _timeSeriesDefinition;

	public static Type Type => typeof(ObservableSpatiallyVariableTimeSeriesPayload);

	public override SpatiallyVariableTimeSeriesPayload Payload => _payload as SpatiallyVariableTimeSeriesPayload;

	public override string FriendlyName => "Spatially Variable Time Series";

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

	public BindableMemoryWrapper<double> Stations => field ?? (field = new BindableMemoryWrapper<double>(Payload.Stations, (BindableMemoryWrapper<double>.SetValueArgs args) => Payload.Stations = args.NewValues, 0.0, isFixedSize: false));

	public AdvancedCollection<BindableMemoryWrapper<double>> Values
	{
		get
		{
			if (field == null)
			{
				field = new AdvancedCollection<BindableMemoryWrapper<double>>();
				foreach (Memory<double> localValue in Payload.Values)
				{
					BindableMemoryWrapper<double> setValueArgsIndexOfTerm = new BindableMemoryWrapper<double>(localValue, (BindableMemoryWrapper<double>.SetValueArgs args) =>
					{
						int indexOf = field.IndexOf(args.Sender);
						if (indexOf < 0 || indexOf >= Payload.Values.Count)
						{
							throw new IndexOutOfRangeException("Updating timestep index that doesn't exist.");
						}
						Payload.Values[indexOf] = args.NewValues;
						return args.NewValues;
					}, double.NaN, isFixedSize: false);
					field.Add(setValueArgsIndexOfTerm);
				}
			}
			return field;
		}
	}

	public int Count => Values.Count;

	public ObservableSpatiallyVariableTimeSeriesPayload(SpatiallyVariableTimeSeriesPayload payload)
		: base(payload)
	{
	}

	public void AddNewStation()
	{
		foreach (BindableMemoryWrapper<double> bindableMemoryWrapper in Values)
		{
			bindableMemoryWrapper.Add(double.NaN);
		}
		Stations.Add(-1.0);
	}

	public void InterpolateData()
	{
		foreach (BindableMemoryWrapper<double> bindableMemoryWrapper in Values)
		{
			ListHelpers.LinearInterpolateNaNsInPlace(bindableMemoryWrapper, bindableMemoryWrapper.Count, (BindableMemoryWrapper<double> m, int idx) => m[idx], (BindableMemoryWrapper<double> m, int idx, double computed) =>
			{
				m[idx] = computed;
			});
		}
	}

	public void Clear()
	{
		foreach (BindableMemoryWrapper<double> bindableMemoryWrapper in Values)
		{
			for (int i = 0; i < bindableMemoryWrapper.Count; i++)
			{
				bindableMemoryWrapper[i] = double.NaN;
			}
		}
	}
}
