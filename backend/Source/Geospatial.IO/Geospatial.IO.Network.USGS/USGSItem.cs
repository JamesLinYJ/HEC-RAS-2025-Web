using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Geospatial.IO.Network.USGS;

[DebuggerDisplay("{Title}")]
public class USGSItem
{
	[JsonPropertyName("title")]
	public string Title { get; set; }

	[JsonPropertyName("downloadURL")]
	public string DownloadURL { get; set; }

	[JsonPropertyName("sizeInBytes")]
	public long? SizeInBytesNullable { get; set; }

	[JsonIgnore]
	public long SizeInBytes
	{
		get
		{
			return SizeInBytesNullable ?? 0;
		}
		set
		{
			SizeInBytesNullable = value;
		}
	}

	[JsonPropertyName("publicationDate")]
	public string PublicationDate { get; set; }

	[JsonPropertyName("extent")]
	public string Extent { get; set; }

	[JsonIgnore]
	public DataResolution Resolution { get; set; }

	[JsonPropertyName("metaUrl")]
	public string MetaURL { get; set; }

	[JsonPropertyName("boundingBox")]
	public BoundingBoxJson BoundingBox { get; set; }
}
