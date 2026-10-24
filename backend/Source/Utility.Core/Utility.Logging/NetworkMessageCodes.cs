namespace Utility.Logging;

public static class NetworkMessageCodes
{
	public static readonly MessageClass DownloadError = new MessageClass("Network_DownloadError", MessageSeverity.Error, "Download failed for {URL}.");

	public static readonly MessageClass DownloadDetailError = new MessageClass("Network_DownloadDetailError", MessageSeverity.Error, "Download failed for {URL} ({Detail}).");

	public static readonly MessageClass DownloadStatusError = new MessageClass("Network_DownloadStatusError", MessageSeverity.Error, "Download failed for {URL} (HTTP {StatusCode}).");

	public static readonly MessageClass FWLinkResolutionError = new MessageClass("Network_FWLinkResolutionError", MessageSeverity.Error, "Failed to resolve fwlink for {URL}.");

	public static readonly MessageClass FetchRetry = new MessageClass("Network_FetchRetry", MessageSeverity.Information, "Fetch failed ({Reason}); retrying ({Attempt} of {Max})…");

	public static Message GetDownloadError(string url)
	{
		return DownloadError.Create(url);
	}

	public static Message GetDownloadError(string url, string detail)
	{
		return DownloadDetailError.Create(url, detail);
	}

	public static Message GetDownloadStatusError(string url, int statusCode)
	{
		return DownloadStatusError.Create(url, statusCode);
	}

	public static Message GetFWLinkResolutionError(string url)
	{
		return FWLinkResolutionError.Create(url);
	}

	public static Message GetFetchRetry(string reason, int attempt, int max)
	{
		return FetchRetry.Create(reason, attempt, max);
	}
}
