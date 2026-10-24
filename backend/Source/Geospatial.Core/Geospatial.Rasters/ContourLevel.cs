namespace Geospatial.Rasters;

public readonly struct ContourLevel(float floatValue, bool isMajor)
{
	public readonly float Value = floatValue;

	public readonly bool IsMajor = isMajor;
}
