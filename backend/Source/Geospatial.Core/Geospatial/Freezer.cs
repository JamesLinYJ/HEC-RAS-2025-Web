using System.Threading;

namespace Geospatial;

public struct Freezer
{
	public int FrozenCount;

	public long FrozenVersionXY;

	public bool IsFrozen => FrozenCount != 0;

	public void FreezeOne<T>(T localValue) where T : ISpatialVersion
	{
		if (FrozenCount == 0)
		{
			FrozenVersionXY = localValue.VersionXY;
		}
		Interlocked.Increment(ref FrozenCount);
	}

	public void FreezeOne(long currentVersion)
	{
		if (FrozenCount == 0)
		{
			FrozenVersionXY = currentVersion;
		}
		Interlocked.Increment(ref FrozenCount);
	}

	public void ThawOne()
	{
		Interlocked.Decrement(ref FrozenCount);
	}
}
