namespace Utility.Memory;

public interface ITimedCacheItem
{
	long LastAccessedTime { get; }

	long ItemSize { get; }
}
