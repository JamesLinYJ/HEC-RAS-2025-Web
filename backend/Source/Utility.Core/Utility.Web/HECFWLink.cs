using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Utility.Web;

public static class HECFWLink
{
	private static readonly HttpClient _client = new HttpClient();

	public const string RootURL = "https://www.hec.usace.army.mil/fwlink/?linkid=";

	public const string AsStringSuffix = "&type=string";

	public static string BuildURL(string linkID)
	{
		return "https://www.hec.usace.army.mil/fwlink/?linkid=" + linkID;
	}

	public static async Task<string> GetLinkIDAsRawString(string linkID)
	{
		string getIds = BuildURL(linkID);
		getIds += "&type=string";
		HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, getIds);
		HttpResponseMessage httpResponseMessage = await _client.SendAsync(request);
		if (httpResponseMessage.IsSuccessStatusCode)
		{
			return await httpResponseMessage.Content.ReadAsStringAsync();
		}
		throw new Exception("Bad Request");
	}

	public static async Task<(bool, string)> TryGetLinkIDAsRawString(string linkID)
	{
		_ = 1;
		try
		{
			string getIds = BuildURL(linkID);
			getIds += "&type=string";
			HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, getIds);
			HttpResponseMessage httpResponseMessage = await _client.SendAsync(request);
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				string trim = (await httpResponseMessage.Content.ReadAsStringAsync()).Trim();
				if (trim.StartsWith("<!DOCTYPE"))
				{
					return (false, "");
				}
				return (true, trim.Trim());
			}
			return (false, "");
		}
		catch
		{
			return (false, "");
		}
	}
}
