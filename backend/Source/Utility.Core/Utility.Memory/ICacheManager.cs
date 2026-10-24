using System;

namespace Utility.Memory;

public interface ICacheManager
{
	int MaximumCacheSize { get; set; }

	int CurrentCacheSize { get; }

	void EvictAll();

	void EvictNeeded();
}
public interface ICacheManager<TKey, TValue>
{
	TValue GetOrMake(TKey key, Func<TValue> generator);

	ICacheManager<TKeyTransform, TValue> GetView<TKeyTransform>(Func<TKeyTransform, TKey> keyTransformer);
}
