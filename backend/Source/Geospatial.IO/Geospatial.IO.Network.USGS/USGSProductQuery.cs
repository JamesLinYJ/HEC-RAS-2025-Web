using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Utility.Logging;

namespace Geospatial.IO.Network.USGS;

[DebuggerDisplay("{Total} items")]
public class USGSProductQuery
{
	[JsonPropertyName("total")]
	public int Total { get; set; }

	[JsonPropertyName("items")]
	public List<USGSItem> Items { get; set; }

	[JsonPropertyName("errorMessage")]
	public string ErrorMessage { get; set; }

	[JsonIgnore]
	public OperationResult OperationResult { get; set; } = new OperationResult();
}
