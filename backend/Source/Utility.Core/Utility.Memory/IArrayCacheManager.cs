using System;

namespace Utility.Memory;

public interface IArrayCacheManager<TKey, TValue> where TValue : unmanaged
{
	int MaxCacheSizeBytes { get; set; }

	string LogIdentifier { get; set; }

	void BuildOrUseExisting(TKey key, int size, Action<Memory<TValue>> populate, Action<Memory<TValue>> operate);

	void BuildOrUseExisting<TModel>(TKey key, int size, TModel model, Action<TModel, Memory<TValue>> populate, Action<TModel, Memory<TValue>> operate);

	bool TryUseExistingOrBuildPinned<TModel>(TKey key, int size, TModel model, Action<TModel, Memory<TValue>> populate, out ReadOnlyMemory<TValue> data);

	void Unpin(TKey key);

	void BuildOrUseExisting(TKey key, int size, Func<Memory<TValue>, bool> populate, Action<Memory<TValue>, bool> operate);

	void ForceEvictAll();

	IArrayCacheManager<TKeyTransform, TValue> GetView<TKeyTransform>(Func<TKeyTransform, TKey> keyTransformer);
}
