namespace Utility.Web;

public class RequestParameter
{
	public const string DELIMITER = "=";

	private string Key { get; }

	private string Value { get; }

	public RequestParameter(string key, string stringValue)
	{
		Key = key;
		Value = stringValue;
	}

	public override string ToString()
	{
		return Key + "=" + Value;
	}
}
