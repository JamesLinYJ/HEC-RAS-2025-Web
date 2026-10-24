namespace Utility.Memory;

public interface ISizeKnown
{
	long Size { get; }

	long CalculateSize();
}
