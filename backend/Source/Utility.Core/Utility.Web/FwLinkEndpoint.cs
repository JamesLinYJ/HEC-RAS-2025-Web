using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Utility.Web;

public sealed class FwLinkEndpoint
{
	private static readonly ILogger _logger = LogHelper.GetLogger("FwLinkEndpoint");

	private readonly string _description;

	private string _url;

	public string LinkID { get; }

	public FwLinkEndpoint(string linkID, string description)
	{
		LinkID = linkID;
		_description = description;
	}

	public async Task<(bool Success, string Url)> TryGetUrlAsync()
	{
		if (_url != null)
		{
			return (Success: true, Url: _url);
		}
		var (localValue, localValue2) = await HECFWLink.TryGetLinkIDAsRawString(LinkID);
		if (!localValue || string.IsNullOrWhiteSpace(localValue2))
		{
			_logger.LogError("Could not resolve fwlink {LinkID} for {Service}.", LinkID, _description);
			return (Success: false, Url: null);
		}
		string url = localValue2.Trim().TrimEnd('/');
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri result) || (result.Scheme != Uri.UriSchemeHttp && result.Scheme != Uri.UriSchemeHttps))
		{
			_logger.LogError("fwlink {LinkID} for {Service} did not resolve to an absolute http(s) URL.", LinkID, _description);
			return (Success: false, Url: null);
		}
		_url = url;
		return (Success: true, Url: _url);
	}

	public async Task<string> GetUrlOrThrowAsync()
	{
		var (localValue, result) = await TryGetUrlAsync();
		if (!localValue)
		{
			throw new InvalidOperationException($"Failed to resolve fwlink \"{LinkID}\" for {_description}.");
		}
		return result;
	}
}
