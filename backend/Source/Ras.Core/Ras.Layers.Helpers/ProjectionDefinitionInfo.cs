namespace Ras.Layers.Helpers;

public class ProjectionDefinitionInfo
{
	public ProjectionType Type { get; set; }

	public string Value { get; set; }

	public ProjectionDefinitionInfo()
	{
	}

	public ProjectionDefinitionInfo(ProjectionDefinition projDef)
	{
		Type = projDef.Type;
		Value = projDef.Value;
	}
}
