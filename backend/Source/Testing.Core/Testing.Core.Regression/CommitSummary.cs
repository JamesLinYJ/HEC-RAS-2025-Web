using System.Text.Json.Serialization;

namespace Testing.Core.Regression;

public class CommitSummary
{
	public string Id { get; set; }

	public string DisplayId { get; set; }

	public string Message { get; set; }

	[JsonPropertyName("CommitterTimestamp")]
	public long CommitterUnixTimestamp { get; set; }
}
