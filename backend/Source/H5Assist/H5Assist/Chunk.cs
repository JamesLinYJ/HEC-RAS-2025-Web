using System;

namespace H5Assist;

public abstract class Chunk<T>
{
	private long _dateLastAccessed;

	public long GetLastAccessedDate()
	{
		return _dateLastAccessed;
	}

	public void UpdateLastAccessedDate()
	{
		_dateLastAccessed = DateTime.Now.Ticks;
	}

	public abstract int GetChunkSize();
}
