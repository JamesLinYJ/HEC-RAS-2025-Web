using System.Collections.Generic;
using System.Text.Json;

namespace Ras.Updates;

public class ExampleProjectCollection
{
	public const string CurrentVersion = "1.0";

	public string Version { get; set; } = "1.0";

	public string Name { get; set; }

	public List<ExampleProjectDetails> AllProjects { get; set; }

	public ExampleProjectCollection()
	{
		AllProjects = new List<ExampleProjectDetails>();
	}

	public string SerializeJSON()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	public static ExampleProjectCollection DeserializeJSON(string json)
	{
		return JsonSerializer.Deserialize<ExampleProjectCollection>(json);
	}
}
