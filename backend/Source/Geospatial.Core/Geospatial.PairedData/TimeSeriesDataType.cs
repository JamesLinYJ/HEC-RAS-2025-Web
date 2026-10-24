using Utility.Extensions;

namespace Geospatial.PairedData;

public enum TimeSeriesDataType
{
	[DiskName("Rate")]
	Rate,
	[DiskName("Amount")]
	Amount,
	[DiskName("Delta")]
	Delta
}
