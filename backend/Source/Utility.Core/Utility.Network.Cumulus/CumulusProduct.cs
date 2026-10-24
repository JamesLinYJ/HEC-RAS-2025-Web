using System;
using System.Text.Json.Serialization;

namespace Utility.Network.Cumulus;

public sealed class CumulusProduct
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("slug")]
	public string Slug { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("label")]
	public string Label { get; set; }

	[JsonPropertyName("parameter")]
	public string Parameter { get; set; }

	[JsonPropertyName("unit")]
	public string Unit { get; set; }

	[JsonPropertyName("temporal_resolution")]
	public int TemporalResolution { get; set; }

	[JsonPropertyName("temporal_duration")]
	public int TemporalDuration { get; set; }

	[JsonPropertyName("dss_datatype")]
	public string DssDatatype { get; set; }

	[JsonPropertyName("after")]
	public DateTime? After { get; set; }

	[JsonPropertyName("before")]
	public DateTime? Before { get; set; }

	public string DisplayName
	{
		get
		{
			if (string.IsNullOrEmpty(Name))
			{
				if (string.IsNullOrEmpty(Label))
				{
					return Slug;
				}
				return Label;
			}
			return Name;
		}
	}

	public bool IsPrecip => string.Equals(Parameter, "PRECIP", StringComparison.OrdinalIgnoreCase);

	public override string ToString()
	{
		return DisplayName;
	}
}
