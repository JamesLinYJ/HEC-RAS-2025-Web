using System;

namespace Ras.Updates;

public class ReleaseChannelDetailsAttribute : Attribute
{
	public string Server { get; set; } = "https://s3.hecdev.net";

	public string Bucket { get; set; } = "ras-public-releases";

	public bool RequiresDevAuth { get; set; }

	public bool IsEnabled { get; set; } = true;
}
