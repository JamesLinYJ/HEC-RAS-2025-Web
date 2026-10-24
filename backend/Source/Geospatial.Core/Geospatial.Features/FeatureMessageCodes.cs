using Utility.Logging;

namespace Geospatial.Features;

public static class FeatureMessageCodes
{
	public static readonly MessageClass BadOrMissingFeatureData = new MessageClass("Feature_BadFeatureData", MessageSeverity.Error, "{FeatureName} has bad or missing feature data.");

	public static readonly MessageClass BadOrMissingFeatureDataWithFID = new MessageClass("Feature_BadFeatureDataFID", MessageSeverity.Error, "{FeatureName} (FID[{FID}]) has bad or missing feature data.");

	public static Message GetBadOrMissingFeatureData(string name)
	{
		return BadOrMissingFeatureData.Create(name);
	}

	public static Message GetBadOrMissingFeatureData(string name, int fid)
	{
		return BadOrMissingFeatureDataWithFID.Create(name, fid);
	}
}
