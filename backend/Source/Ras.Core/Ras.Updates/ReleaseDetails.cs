using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ras.Updates;

public class ReleaseDetails
{
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public ReleaseType Type { get; set; }

	public string Name { get; set; }

	public string Version { get; set; }

	[JsonIgnore]
	public string PrettierVersion => Version.Split('+')[0];

	[JsonIgnore]
	public bool IsLatest { get; set; }

	public string Server { get; set; }

	public string Bucket { get; set; }

	public string ManifestJSONKey { get; set; }

	public DateTime ReleaseDate { get; set; }

	public string ReleaseNotesURL { get; set; }

	public string ReleaseNotesSummary { get; set; }

	public string SerializeJSON()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	public static ReleaseDetails DeserializeJSON(string json)
	{
		return JsonSerializer.Deserialize<ReleaseDetails>(json);
	}
}
