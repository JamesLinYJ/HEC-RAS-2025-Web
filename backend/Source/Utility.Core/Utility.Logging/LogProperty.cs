namespace Utility.Logging;

public readonly struct LogProperty(string name, object localValue)
{
	public readonly string Name = name;

	public readonly object Value = localValue;

	public override string ToString()
	{
		return $"{Name}={Value}";
	}
}
