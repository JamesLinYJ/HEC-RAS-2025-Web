using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Ras.Layers.Payloads;
using Utility.Logging;

namespace Ras.Observables;

public abstract class ObservablePayload : ObservableValidator
{
	public delegate void IsEnabledChangedEvent(ObservablePayload sender, bool isEnabled);

	private static readonly ILogger _logger = LogHelper.GetLogger("ObservablePayload");

	protected readonly PayloadBase _payload;

	public virtual PayloadBase Payload => _payload;

	public string Name
	{
		get
		{
			return _payload.Name;
		}
		set
		{
			SetProperty(_payload.Name, value, _payload, (PayloadBase r, string v) =>
			{
				r.Name = v;
			}, "Name");
		}
	}

	public string Description
	{
		get
		{
			return _payload.Description;
		}
		set
		{
			SetProperty(_payload.Description, value, _payload, (PayloadBase r, string v) =>
			{
				r.Description = v;
			}, "Description");
		}
	}

	public string Variable => _payload.Variable;

	public bool IsEnabled
	{
		get
		{
			return _payload.IsEnabled;
		}
		set
		{
			if (SetProperty(_payload.IsEnabled, value, _payload, (PayloadBase r, bool v) =>
			{
				r.IsEnabled = v;
			}, "IsEnabled"))
			{
				IsEnabledChanged?.Invoke(this, value);
			}
		}
	}

	public abstract string FriendlyName { get; }

	public event IsEnabledChangedEvent IsEnabledChanged;

	public ObservablePayload(PayloadBase payload)
	{
		_payload = payload;
	}

	public static ObservablePayload From(ObservableProject project, PayloadBase payload)
	{
		return payload.Switch((Func<ConstantPayload, ObservablePayload>)((ConstantPayload cpl) => new ObservableConstantPayload(cpl)), (Func<TimeSeriesPayload, ObservablePayload>)((TimeSeriesPayload tspl) => new ObservableTimeSeriesPayload(tspl)), (Func<RatingCurvePayload, ObservablePayload>)((RatingCurvePayload rcpl) => new ObservableRatingCurvePayload(rcpl)), (Func<FunctionalPayload, ObservablePayload>)((FunctionalPayload fpl) => new ObservableFunctionalPayload(fpl)), (Func<ExternalPayload, ObservablePayload>)((ExternalPayload epl) => new ObservableExternalPayload(project, epl)), (Func<SpatiallyVariableTimeSeriesPayload, ObservablePayload>)((SpatiallyVariableTimeSeriesPayload svtpl) => new ObservableSpatiallyVariableTimeSeriesPayload(svtpl)));
	}
}
