using System;
using System.IO;
using System.Text.Json;
using Utility.Units;

namespace Ras.Layers.Helpers;

public class ProjectInfo : IJsonSerializable
{
	public const string CurrentSchemaVersion = "1.0.0";

	public static readonly JsonSerializerOptions DefaultSerializerOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	public string SchemaVersion { get; set; } = "1.0.0";

	public string Name { get; set; }

	public DateTime DateLastModified { get; set; }

	public string Description { get; set; }

	public UnitSystem UnitSystem { get; set; }

	public ProjectionDefinitionInfo Projection { get; set; }

	public GeometryInfo[] Geometries { get; set; }

	public PlanInfo[] Plans { get; set; }

	public ResultInfo[] Results { get; set; }

	public static ProjectInfo From(string filename, bool exploreProject = false)
	{
		Project project = new Project(filename);
		return new ProjectInfo
		{
			Name = project.Name,
			DateLastModified = new FileInfo(filename).LastWriteTime,
			Description = project.Description,
			UnitSystem = project.Units,
			Projection = new ProjectionDefinitionInfo(project.ProjectionDefinition)
		};
	}

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, DefaultSerializerOptions);
	}
}
