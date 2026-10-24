namespace Geospatial.PairedData;

public struct TimeSeriesDataBehavior
{
	public bool AnyPositive;

	public bool AnyNegative;

	public bool AnyNoData;

	public bool AnyZero;

	public bool AllZero
	{
		get
		{
			if (AnyZero && !AnyNegative)
			{
				return !AnyPositive;
			}
			return false;
		}
	}
}
