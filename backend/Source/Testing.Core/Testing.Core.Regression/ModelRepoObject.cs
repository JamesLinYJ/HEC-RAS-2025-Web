using System;

namespace Testing.Core.Regression;

public class ModelRepoObject
{
	public string ObjectType { get; set; }

	public int Id { get; set; }

	public string Name { get; set; }

	public int ObjectVersion { get; set; }

	public string RasVersion { get; set; }

	public string S3Key { get; set; }

	public bool Archived { get; set; }

	public long UnixPublishDate { get; set; }

	public string Folder { get; set; }

	public string Description { get; set; }

	public string ContentHash { get; set; }

	public static string TypeEnumToString(ModelRepoObjectType type)
	{
		return type.ToString().ToLower();
	}

	public static ModelRepoObjectType TypeEnumFromString(string type)
	{
		if (!Enum.TryParse<ModelRepoObjectType>(char.ToUpper(type[0]) + type.Substring(1), out var result))
		{
			throw new ArgumentException("Bad object type");
		}
		return result;
	}
}
