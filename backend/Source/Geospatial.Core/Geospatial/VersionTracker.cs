namespace Geospatial;

public struct VersionTracker
{
	public const long InitialVersion = 1L;

	private long _versionStore;

	public void Assign<T>(ref T currentItem, T newValue) where T : IMutableSpatialVersion
	{
		if ((object)currentItem == (object)newValue)
		{
			return;
		}
		if (_versionStore == 0L)
		{
			_versionStore = 1L;
		}
		if (newValue == null)
		{
			_versionStore = currentItem.VersionXY + 1;
		}
		else
		{
			if (currentItem == null)
			{
				long versionStoreOffset = _versionStore + 1;
				newValue.ForceSetVersion(versionStoreOffset);
			}
			else
			{
				newValue.ForceSetVersion(currentItem.VersionXY + 1);
			}
			_versionStore = 1L;
		}
		currentItem = newValue;
	}

	public long GetVersion(ISpatialVersion currentItem)
	{
		if (_versionStore == 0L)
		{
			_versionStore = 1L;
		}
		return currentItem?.VersionXY ?? _versionStore;
	}
}
