using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public class LogAggregator
{
	private readonly ILogger _logger;

	private readonly string _msg;

	private readonly int _autoReportOnCount;

	private int _runningCount;

	private double _runningTimeMs;

	public LogAggregator(ILogger logger, string msg, int reportOnCount)
	{
		_logger = logger;
		_msg = msg;
		_autoReportOnCount = reportOnCount;
	}

	public void Add(Stopwatch sw)
	{
		lock (this)
		{
			_runningTimeMs += sw.Elapsed.TotalMilliseconds;
			_runningCount++;
			if (_runningCount >= _autoReportOnCount)
			{
				double runningTimeMsOverRunningCount = _runningTimeMs / (double)_runningCount;
				if (runningTimeMsOverRunningCount > 10.0)
				{
					runningTimeMsOverRunningCount = Math.Round(runningTimeMsOverRunningCount, 0);
				}
				else
				{
					runningTimeMsOverRunningCount = ((!(runningTimeMsOverRunningCount > 1.0)) ? Math.Round(runningTimeMsOverRunningCount, 2) : Math.Round(runningTimeMsOverRunningCount, 1));
				}
				_logger?.LogDebug(_msg, runningTimeMsOverRunningCount);
				_runningTimeMs = 0.0;
				_runningCount = 0;
			}
		}
	}
}
