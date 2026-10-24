namespace Geospatial.PairedData;

public enum ExtrapolationBehavior
{
	ReturnNoData,
	ReturnZero,
	NearestValue,
	ExtrapolateNearestSegment,
	ThrowException
}
