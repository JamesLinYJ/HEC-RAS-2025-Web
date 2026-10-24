using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Utility.Progress;

namespace Ras.Updates;

public class ReleaseManifest
{
	public ReleaseDetails Details { get; set; }

	public List<MappedFile> Files { get; set; }

	public static ReleaseManifest ForDirectory(string directory, ReleaseDetails details, ProgressReporter prog)
	{
		if (!Directory.Exists(directory))
		{
			throw new DirectoryNotFoundException("directory");
		}
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		List<MappedFile> files = new List<MappedFile>();
		string[] enumerateFiles = Directory.EnumerateFiles(directory, "", SearchOption.AllDirectories).ToArray();
		for (int i = 0; i < enumerateFiles.Length; i++)
		{
			files.Add(MappedFile.ForFile(enumerateFiles[i], directory));
			prog.ReportProgressFraction((float)(i + 1) / (float)enumerateFiles.Length);
		}
		return new ReleaseManifest
		{
			Details = details,
			Files = files
		};
	}

	public string SerializeJSON()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	public static ReleaseManifest DeserializeJSON(string json)
	{
		return JsonSerializer.Deserialize<ReleaseManifest>(json);
	}
}
