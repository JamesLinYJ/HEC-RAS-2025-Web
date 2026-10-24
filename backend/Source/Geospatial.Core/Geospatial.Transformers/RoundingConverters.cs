using System;
using Geospatial.Rasters;

namespace Geospatial.Transformers;

public static class RoundingConverters
{
	public static (RoundBase, int) Convert(RoundTo rounding)
	{
		return rounding switch
		{
			RoundTo.None => (RoundBase.Base10, int.MaxValue), 
			RoundTo.Tenths => (RoundBase.Base10, 1), 
			RoundTo.Hundredths => (RoundBase.Base10, 2), 
			RoundTo.Thousandths => (RoundBase.Base10, 3), 
			RoundTo.Sixteenths => (RoundBase.Base2, 4), 
			RoundTo.ThirtySeconds => (RoundBase.Base2, 5), 
			RoundTo.SixtyFourths => (RoundBase.Base2, 6), 
			RoundTo.HundredTwentyEighths => (RoundBase.Base2, 7), 
			RoundTo.ThousandTwentyFourths => (RoundBase.Base2, 8), 
			_ => throw new NotSupportedException(), 
		};
	}

	public static int RoundBaseToInt(RoundBase b)
	{
		return b switch
		{
			RoundBase.Base2 => 2, 
			RoundBase.Base10 => 10, 
			_ => throw new NotSupportedException(), 
		};
	}
}
