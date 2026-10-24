using System;

namespace Utility.Memory;

internal class CacheView<TKeyBase, TKeyTransform, TValue> : ICacheManager<TKeyTransform, TValue>
{
	private readonly ICacheManager<TKeyBase, TValue> _parent;

	private readonly Func<TKeyTransform, TKeyBase> _keyTransformer;

	public CacheView(ICacheManager<TKeyBase, TValue> parent, Func<TKeyTransform, TKeyBase> keyTransformer)
	{
		_parent = parent;
		_keyTransformer = keyTransformer;
	}

	public TValue GetOrMake(TKeyTransform key, Func<TValue> generator)
	{
		TKeyBase key2 = _keyTransformer(key);
		return _parent.GetOrMake(key2, generator);
	}

	public ICacheManager<TKeyTransformInner, TValue> GetView<TKeyTransformInner>(Func<TKeyTransformInner, TKeyTransform> keyTransformer)
	{
		return new CacheView<TKeyTransform, TKeyTransformInner, TValue>(this, keyTransformer);
	}
}
