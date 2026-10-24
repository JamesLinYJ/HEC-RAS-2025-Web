namespace Ras.Migration.Native;

public struct NamedValue(string n, float v)
{
	public string Name = n;

	public float Value = v;
}
