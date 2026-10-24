using System;
using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public abstract class RollingFileSinkOptions
{
	public string Path { get; set; }

	public LogLevel MinimumLevel { get; set; }

	public long MaxFileSizeBytes { get; set; } = 67108864L;

	public int MaxRetainedFiles { get; set; } = 5;

	public bool RollOnStartup { get; set; }

	public int BufferBytes { get; set; } = 65536;

	public bool SharedAccess { get; set; } = true;

	public int MaxAlternatePaths { get; set; } = 3;

	public TimeSpan ReopenInterval { get; set; } = TimeSpan.FromSeconds(10L);
}
