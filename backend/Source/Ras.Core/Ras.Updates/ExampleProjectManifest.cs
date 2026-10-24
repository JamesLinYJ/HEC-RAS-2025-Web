using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Utility.Progress;

namespace Ras.Updates;

public class ExampleProjectManifest
{
	public ExampleProjectDetails Details { get; set; }

	public List<MappedFile> Files { get; set; }

	public static ExampleProjectManifest ForDirectory(string directory, ExampleProjectDetails details, ProgressReporter prog)
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
			if (!(Path.GetExtension(enumerateFiles[i]) == ".lock"))
			{
				files.Add(MappedFile.ForFile(enumerateFiles[i], directory));
				prog.ReportProgressFraction((float)(i + 1) / (float)enumerateFiles.Length);
			}
		}
		return new ExampleProjectManifest
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

	public static ExampleProjectManifest DeserializeJSON(string json)
	{
		return JsonSerializer.Deserialize<ExampleProjectManifest>(json);
	}
}
