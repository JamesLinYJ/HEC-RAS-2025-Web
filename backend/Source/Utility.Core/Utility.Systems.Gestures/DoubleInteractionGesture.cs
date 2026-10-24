using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Utility.Systems.Gestures;

public class DoubleInteractionGesture
{
	private int _version;

	private long _lastStartTime;

	private long _lastCompleteTime;

	private bool _waitingForInitialGesture;

	private bool _waitingForDoubleGesture;

	private bool _didDoubleGesture;

	private bool _taskInterjected;

	private Point _lastStartPoint;

	private readonly int _stallTimeMS;

	private readonly bool _segregatedActions;

	private readonly double _maximumDoubleGestureDriftDistance;

	public const int DefaultBeginDragThresholdPixels = 10;

	public DoubleInteractionGesture(int stallTimeMS, bool segregatedActions = true, double maximumDoubleGestureDriftDistance = double.MaxValue)
	{
		_stallTimeMS = stallTimeMS;
		_segregatedActions = segregatedActions;
		_maximumDoubleGestureDriftDistance = maximumDoubleGestureDriftDistance;
	}

	private static long GetCurrentTime()
	{
		return DateTime.UtcNow.Ticks / 10000;
	}

	public async ValueTask<GestureResult> InteractionStart(Point position = default(Point))
	{
		_version++;
		_lastStartTime = GetCurrentTime();
		if (_waitingForDoubleGesture && _lastStartTime - _lastCompleteTime < _stallTimeMS)
		{
			_waitingForDoubleGesture = false;
			_didDoubleGesture = true;
			if (Distance(_lastStartPoint, position) < _maximumDoubleGestureDriftDistance)
			{
				return GestureResult.Double;
			}
			return GestureResult.Single;
		}
		int lastVersion = _version;
		_lastStartPoint = position;
		_taskInterjected = false;
		_waitingForInitialGesture = true;
		await Task.Delay(_stallTimeMS);
		_waitingForInitialGesture = false;
		if (_taskInterjected)
		{
			_taskInterjected = false;
			_waitingForDoubleGesture = false;
			return GestureResult.Ignore;
		}
		if (_lastCompleteTime < _lastStartTime && lastVersion == _version)
		{
			return GestureResult.HoldStart;
		}
		return GestureResult.Ignore;
	}

	public GestureResult InteractionInterjection(double pixelDelta)
	{
		if (_didDoubleGesture || !_waitingForInitialGesture || _taskInterjected)
		{
			return GestureResult.InterjectionIgnored;
		}
		if (pixelDelta < 10.0)
		{
			return GestureResult.InterjectionIgnored;
		}
		_taskInterjected = true;
		_didDoubleGesture = false;
		_waitingForDoubleGesture = false;
		return GestureResult.InterjectionSucceeded;
	}

	public async ValueTask<GestureResult> InteractionCompleted()
	{
		_version++;
		_lastCompleteTime = GetCurrentTime();
		if (_didDoubleGesture)
		{
			_didDoubleGesture = false;
			return GestureResult.Ignore;
		}
		if (_lastCompleteTime - _lastStartTime < _stallTimeMS)
		{
			bool taskInterjected = _taskInterjected;
			_waitingForDoubleGesture = true;
			_taskInterjected = false;
			if (!_segregatedActions)
			{
				if (taskInterjected)
				{
					return GestureResult.HoldEnd;
				}
				return GestureResult.Single;
			}
			await Task.Delay(_stallTimeMS);
			if (_taskInterjected)
			{
				_taskInterjected = false;
				return GestureResult.Ignore;
			}
			if (_lastStartTime < _lastCompleteTime && _waitingForDoubleGesture)
			{
				return GestureResult.Single;
			}
			return GestureResult.Ignore;
		}
		return GestureResult.HoldEnd;
	}

	public double Distance(Point p1, Point p2)
	{
		int p2P1Term = p2.X - p1.X;
		int p2P1Term2 = p2.Y - p1.Y;
		return Math.Sqrt(p2P1Term * p2P1Term + p2P1Term2 * p2P1Term2);
	}
}
