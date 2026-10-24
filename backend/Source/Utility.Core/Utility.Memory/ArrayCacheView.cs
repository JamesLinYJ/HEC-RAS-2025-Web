using System;

namespace Utility.Memory;

internal class ArrayCacheView<TKeyBase, TKeyTransform, TValue> : IArrayCacheManager<TKeyTransform, TValue> where TValue : unmanaged
{
	private readonly IArrayCacheManager<TKeyBase, TValue> _parent;

	private readonly Func<TKeyTransform, TKeyBase> _keyTransformer;

	public string LogIdentifier
	{
		get
		{
			return _parent.LogIdentifier;
		}
		set
		{
			_parent.LogIdentifier = value;
		}
	}

	public int MaxCacheSizeBytes
	{
		get
		{
			return _parent.MaxCacheSizeBytes;
		}
		set
		{
			_parent.MaxCacheSizeBytes = value;
		}
	}

	public ArrayCacheView(IArrayCacheManager<TKeyBase, TValue> parent, Func<TKeyTransform, TKeyBase> keyTransformer)
	{
		_parent = parent;
		_keyTransformer = keyTransformer;
	}

	public void BuildOrUseExisting(TKeyTransform key, int size, Action<Memory<TValue>> populate, Action<Memory<TValue>> operate)
	{
		TKeyBase key2 = _keyTransformer(key);
		_parent.BuildOrUseExisting(key2, size, populate, operate);
	}

	public void BuildOrUseExisting<TModel>(TKeyTransform key, int size, TModel model, Action<TModel, Memory<TValue>> populate, Action<TModel, Memory<TValue>> operate)
	{
		TKeyBase key2 = _keyTransformer(key);
		_parent.BuildOrUseExisting(key2, size, model, populate, operate);
	}

	public bool TryUseExistingOrBuildPinned<TModel>(TKeyTransform key, int size, TModel model, Action<TModel, Memory<TValue>> populate, out ReadOnlyMemory<TValue> data)
	{
		TKeyBase key2 = _keyTransformer(key);
		return _parent.TryUseExistingOrBuildPinned(key2, size, model, populate, out data);
	}

	public void Unpin(TKeyTransform key)
	{
		_parent.Unpin(_keyTransformer(key));
	}

	public void BuildOrUseExisting(TKeyTransform key, int size, Func<Memory<TValue>, bool> populate, Action<Memory<TValue>, bool> operate)
	{
		TKeyBase key2 = _keyTransformer(key);
		_parent.BuildOrUseExisting(key2, size, populate, operate);
	}

	public void ForceEvictAll()
	{
		_parent.ForceEvictAll();
	}

	public IArrayCacheManager<TKeyTransformInner, TValue> GetView<TKeyTransformInner>(Func<TKeyTransformInner, TKeyTransform> keyTransformer)
	{
		return new ArrayCacheView<TKeyTransform, TKeyTransformInner, TValue>(this, keyTransformer);
	}
}
