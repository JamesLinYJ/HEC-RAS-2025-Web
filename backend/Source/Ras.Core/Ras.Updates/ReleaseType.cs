using Utility.Attributes;

namespace Ras.Updates;

public enum ReleaseType
{
	[ReleaseChannelDetails(IsEnabled = false)]
	Custom,
	[ReleaseChannelDetails(Bucket = "ras-public-releases-staging", RequiresDevAuth = true)]
	Staging,
	[ReleaseChannelDetails(Bucket = "ras-live-builds", RequiresDevAuth = true)]
	[EnumDisplayName("Live-Dev")]
	Nightly,
	[ReleaseChannelDetails(Bucket = "ras-public-releases", IsEnabled = false)]
	Alpha,
	[ReleaseChannelDetails(Bucket = "ras-public-releases")]
	Beta,
	[ReleaseChannelDetails(Bucket = "ras-official-releases", IsEnabled = false)]
	Official
}
