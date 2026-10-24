using System.Text.Json.Serialization;

namespace Testing.Core.Regression;

public class ModelRepoIdName
{
	public int Id { get; set; }

	public string Name { get; set; }

	public string Folder { get; set; }

	[JsonPropertyName("objectType")]
	public string ObjectTypeName { get; set; }

	[JsonIgnore]
	public ModelRepoObjectType ObjectType => ModelRepoObject.TypeEnumFromString(ObjectTypeName);
}
