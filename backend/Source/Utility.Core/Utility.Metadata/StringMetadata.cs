namespace Utility.Metadata;

public class StringMetadata : IStringMetaData, IMetadataBase
{
	public string Value { get; }

	public string Key { get; }

	public StringMetadata(string key, string stringValue)
	{
		Key = key;
		Value = stringValue;
	}
}
