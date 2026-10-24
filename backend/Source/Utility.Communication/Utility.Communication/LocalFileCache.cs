using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Utility.Communication;

public class LocalFileCache
{
	public Dictionary<string, FileProxy> _cache;

	public LocalFileCache()
	{
		_cache = new Dictionary<string, FileProxy>();
	}

	public async Task<FileProxy> Acquire(string filename)
	{
		if (!File.Exists(filename))
		{
			return FileProxy.DoesNotExist(filename);
		}
		FileInfo fileInfo = new FileInfo(filename);
		lock (_cache)
		{
			if (_cache.TryGetValue(filename, out var localValue) && localValue.Timestamp == fileInfo.LastWriteTime && localValue.FileSize == fileInfo.Length)
			{
				return localValue;
			}
		}
		FileProxy fileProxy = await FileProxy.LocalFileAsync(filename);
		lock (_cache)
		{
			_cache[filename] = fileProxy;
		}
		return fileProxy;
	}

	public void Clear()
	{
		_cache.Clear();
	}
}
