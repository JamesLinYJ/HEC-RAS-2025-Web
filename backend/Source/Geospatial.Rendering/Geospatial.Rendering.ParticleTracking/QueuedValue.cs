namespace Geospatial.Rendering.ParticleTracking;

public class QueuedValue<T>
{
	private bool _isQueued;

	private T _queuedValue;

	public T Value { get; set; }

	public bool ValueIsQueued => _isQueued;

	public T Queued
	{
		get
		{
			if (!_isQueued)
			{
				return default;
			}
			return _queuedValue;
		}
	}

	public void Set(T v)
	{
		_isQueued = true;
		_queuedValue = v;
	}
}
