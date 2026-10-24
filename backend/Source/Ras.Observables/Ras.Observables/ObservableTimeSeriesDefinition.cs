using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.PairedData;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Ras.Observables;

public class ObservableTimeSeriesDefinition : ObservableValidator
{
	public delegate void FixedStateTimeChangedEvent(ObservableTimeSeriesDefinition sender, DateTime dateTime);

	public delegate void RTSTimeSpanChangedEvent(ObservableTimeSeriesDefinition sender, TimeSpan timeSpan);

	public delegate void UseFixedStartTimeChangedEvent(ObservableTimeSeriesDefinition sender, bool boolValue);

	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableTimeSeriesDefinition");

	private readonly TimeSeriesDefinition _timeSeries;

	public TimeSeriesDefinition TimeSeriesDefinition => _timeSeries;

	public TimeSeriesType TimeSeriesType
	{
		get
		{
			return _timeSeries.TimeSeriesType;
		}
		set
		{
			if (SetProperty(_timeSeries.TimeSeriesType, value, _timeSeries, (TimeSeriesDefinition model, TimeSeriesType timeSeriesType) =>
			{
				model.TimeSeriesType = timeSeriesType;
			}, "TimeSeriesType"))
			{
				NotifyFixedStateTimeChanged();
			}
		}
	}

	public ExpressedTimeType ExpressedTimeType
	{
		get
		{
			return _timeSeries.ExpressedTimeType;
		}
		set
		{
			if (SetProperty(_timeSeries.ExpressedTimeType, value, _timeSeries, (TimeSeriesDefinition model, ExpressedTimeType expressedTimeType) =>
			{
				model.ExpressedTimeType = expressedTimeType;
			}, "ExpressedTimeType"))
			{
				NotifyFixedStateTimeChanged();
			}
		}
	}

	public DateTime FixedStartTime
	{
		get
		{
			return _timeSeries.FixedStartTime;
		}
		set
		{
			if (SetProperty(_timeSeries.FixedStartTime, value, _timeSeries, (TimeSeriesDefinition u, DateTime n) =>
			{
				u.FixedStartTime = n;
			}, "FixedStartTime"))
			{
				NotifyFixedStateTimeChanged();
			}
		}
	}

	public bool UseFixedStartTime
	{
		get
		{
			return _timeSeries.UseFixedStartTime;
		}
		set
		{
			if (SetProperty(_timeSeries.UseFixedStartTime, value, _timeSeries, (TimeSeriesDefinition u, bool n) =>
			{
				u.UseFixedStartTime = n;
			}, "UseFixedStartTime"))
			{
				NotifyFixedStateTimeChanged();
				UseFixedStartTimeChanged?.Invoke(this, value);
			}
		}
	}

	public TimeSpan RTSTimeSpan
	{
		get
		{
			return _timeSeries.RTSTimeSpan;
		}
		set
		{
			if (SetProperty(_timeSeries.RTSTimeSpan, value, _timeSeries, (TimeSeriesDefinition u, TimeSpan n) =>
			{
				u.RTSTimeSpan = n;
			}, "RTSTimeSpan"))
			{
				NotifyRTSTimeSpanChanged();
			}
		}
	}

	public event FixedStateTimeChangedEvent FixedStateTimeChanged;

	public event RTSTimeSpanChangedEvent RTSTimeSpanChanged;

	public event UseFixedStartTimeChangedEvent UseFixedStartTimeChanged;

	public ObservableTimeSeriesDefinition(TimeSeriesDefinition ts)
	{
		_timeSeries = ts;
	}

	public DateTime[] Evaluate(int nOrdinates, DateTime? offset = null)
	{
		try
		{
			OperationResult operationResult = _timeSeries.IsValid(nOrdinates);
			if (!operationResult.Result)
			{
				_logger.LogWarning("Bad TimeSeriesDefinition. {Messages}", operationResult.GetConcatenatedMessages());
				return null;
			}
			return _timeSeries.Evaluate(nOrdinates, offset);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error in Evaluate");
			return null;
		}
	}

	public DateTime EvaluateAtIndex(int rowIdx, int nOrdinates, DateTime? offset = null)
	{
		try
		{
			OperationResult operationResult = _timeSeries.IsValid(nOrdinates);
			if (!operationResult.Result)
			{
				_logger.LogWarning("Bad TimeSeriesDefinition. {Messages}", operationResult.GetConcatenatedMessages());
				return default;
			}
			return _timeSeries.EvaluateAtIndex(rowIdx, nOrdinates, offset);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Error in Evaluate");
			return default;
		}
	}

	private void NotifyFixedStateTimeChanged()
	{
		FixedStateTimeChanged?.Invoke(this, FixedStartTime);
	}

	private void NotifyRTSTimeSpanChanged()
	{
		RTSTimeSpanChanged?.Invoke(this, RTSTimeSpan);
	}
}
