using System;
using System.Text.Json.Serialization;

namespace Utility.Network.Cumulus;

public sealed class CumulusCogFile
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("datetime")]
	public DateTime Datetime { get; set; }

	[JsonPropertyName("version")]
	public DateTime? Version { get; set; }

	[JsonPropertyName("cog_url")]
	public string CogUrl { get; set; }
}
