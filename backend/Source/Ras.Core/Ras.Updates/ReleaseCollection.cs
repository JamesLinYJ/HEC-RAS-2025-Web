using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Ras.Updates;

public class ReleaseCollection
{
	public const string CurrentVersion = "1.0";

	public string Version { get; set; } = "1.0";

	public List<ReleaseDetails> AllReleases { get; set; }

	public ReleaseCollection()
	{
		AllReleases = new List<ReleaseDetails>();
	}

	public void SortReleases()
	{
		AllReleases = AllReleases.OrderByDescending((ReleaseDetails r) => r.ReleaseDate).ToList();
	}

	public string SerializeJSON()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	public static ReleaseCollection DeserializeJSON(string json)
	{
		return JsonSerializer.Deserialize<ReleaseCollection>(json);
	}
}
