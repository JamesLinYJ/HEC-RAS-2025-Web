using System;
using System.Collections.Generic;

namespace RasMapperLib;

public interface IPrecipitationLayer : IMeteorologyLayer
{
	IList<DateTime> BandTimes { get; }
}
