using System;
using System.Collections.Generic;
using Geospatial.Rendering.Systems.Vector;

namespace Geospatial.Rendering.Editing;

public sealed class OverlayChannels
{
	private readonly IVectorManager _vm;

	private readonly Dictionary<string, VectorID> _channels = new Dictionary<string, VectorID>();

	private readonly HashSet<string> _touched = new HashSet<string>();

	public OverlayChannels(IVectorManager vectorManager)
	{
		_vm = vectorManager ?? throw new ArgumentNullException("vectorManager");
	}

	public void BeginFrame()
	{
		_touched.Clear();
	}

	public void EndFrame()
	{
		foreach (KeyValuePair<string, VectorID> channel in new List<KeyValuePair<string, VectorID>>(_channels))
		{
			if (!_touched.Contains(channel.Key))
			{
				VectorID id = channel.Value;
				_vm.TryDeleteVector(ref id);
				_channels.Remove(channel.Key);
			}
		}
	}

	public void ClearAll()
	{
		foreach (KeyValuePair<string, VectorID> channel in _channels)
		{
			VectorID id = channel.Value;
			_vm.TryDeleteVector(ref id);
		}
		_channels.Clear();
		_touched.Clear();
	}

	public bool TryRegisterVector(string channel, VectorID id)
	{
		if (id.IsNull())
		{
			return false;
		}
		RegisterVector(channel, id);
		return true;
	}

	public void RegisterVector(string channel, VectorID id)
	{
		if (id.IsNull())
		{
			throw new InvalidOperationException("Can't register a null vector.");
		}
		if (_channels.TryGetValue(channel, out var localValue))
		{
			if ((long)localValue != (long)id)
			{
				throw new ArgumentException();
			}
			if (!_touched.Contains(channel))
			{
				_touched.Add(channel);
			}
		}
		else
		{
			_channels[channel] = id;
			_touched.Add(channel);
		}
	}

	public void Touch(string channel)
	{
		if (!_touched.Contains(channel))
		{
			_touched.Add(channel);
		}
	}

	public VectorID GetVector(string channel)
	{
		if (!_channels.TryGetValue(channel, out var localValue))
		{
			return 0L;
		}
		return localValue;
	}

	public bool TryGetVector(string channel, out VectorID id)
	{
		return _channels.TryGetValue(channel, out id);
	}

	public bool AreAllVectorIDsNull(params ReadOnlySpan<VectorID> ids)
	{
		ReadOnlySpan<VectorID> idsSpan = ids;
		for (int i = 0; i < idsSpan.Length; i++)
		{
			if (!idsSpan[i].IsNull())
			{
				return false;
			}
		}
		return true;
	}
}
