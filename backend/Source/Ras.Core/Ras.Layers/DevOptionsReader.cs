using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Ras.Layers;

internal class DevOptionsReader
{
	private static readonly ILogger _logger = LogHelper.GetLogger("DevOptionsReader");

	private readonly Dictionary<string, string> _opts;

	private readonly HashSet<string> _consumed;

	internal DevOptionsReader(Dictionary<string, string> opts)
	{
		_opts = opts;
		_consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	public bool HasKey(string key)
	{
		_consumed.Add(key);
		return _opts.ContainsKey(key);
	}

	public bool TryGetValue(string key, out string stringValue)
	{
		_consumed.Add(key);
		return _opts.TryGetValue(key, out stringValue);
	}

	public void WarnUnrecognised(ILogger logger = null)
	{
		ILogger logger2 = logger ?? _logger;
		foreach (string key in _opts.Keys)
		{
			if (!_consumed.Contains(key))
			{
				logger2.LogWarning("DevOptions: unrecognised key '{Key}'", key);
			}
		}
	}
}
