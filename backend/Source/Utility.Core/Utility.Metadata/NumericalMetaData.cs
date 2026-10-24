namespace Utility.Metadata;

public class NumericalMetaData : INumericalMetaData, IMetadataBase
{
	public float Value { get; }

	public string Key { get; }

	public NumericalMetaData(string key, float floatValue)
	{
		Key = key;
		Value = floatValue;
	}
}
