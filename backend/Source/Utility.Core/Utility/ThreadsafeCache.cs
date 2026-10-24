using System;
using System.Collections.Concurrent;

namespace Utility;

public class ThreadsafeCache
{
	private ConcurrentDictionary<object, WeakReference> _cache = new ConcurrentDictionary<object, WeakReference>();
}
