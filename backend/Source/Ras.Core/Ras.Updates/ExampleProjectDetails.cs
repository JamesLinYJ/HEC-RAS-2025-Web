using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Ras.Updates;

public class ExampleProjectDetails
{
	public string Title { get; set; }

	public string Description { get; set; }

	public List<string> Tags { get; set; }

	public string MinimumVersion { get; set; }

	public string MaximumVersion { get; set; }

	public DateTime PublishDate { get; set; }

	public string Server { get; set; }

	public string Bucket { get; set; }

	public string ManifestJSONKey { get; set; }

	public string ThumbnailKey { get; set; }

	public string SerializeJSON()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	public static ExampleProjectDetails DeserializeJSON(string json)
	{
		return JsonSerializer.Deserialize<ExampleProjectDetails>(json);
	}
}
