using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Utility.Web;

public static class SharedWebResourceManager
{
	private static object _clientLock = new object();

	private static HttpClient _client;

	public static HttpClient Client
	{
		get
		{
			if (_client == null)
			{
				lock (_clientLock)
				{
					if (_client == null)
					{
						_client = new HttpClient(new SocketsHttpHandler
						{
							MaxConnectionsPerServer = 1000,
							PooledConnectionLifetime = TimeSpan.FromMinutes(5L),
							PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2L)
						});
						_client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
						_client.DefaultRequestHeaders.UserAgent.ParseAdd("RAS/7.0");
					}
				}
			}
			return _client;
		}
	}

	public static HttpResponseMessage GetSync(string downloadUrl, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, Func<bool> isStillValid = null)
	{
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
		return GetSync(request, option, isStillValid);
	}

	public static HttpResponseMessage GetSync(HttpRequestMessage request, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, Func<bool> isStillValid = null)
	{
		if (isStillValid != null && !isStillValid())
		{
			return null;
		}
		return Client.Send(request, option);
	}

	public static async ValueTask<HttpResponseMessage> GetAsync(string request, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead, Func<bool> isStillValid = null)
	{
		if (isStillValid != null && !isStillValid())
		{
			return null;
		}
		return await Client.GetAsync(request, option);
	}

	public static async ValueTask<HttpResponseMessage> PutAsync(string request, HttpContent content, Func<bool> isStillValid = null)
	{
		if (isStillValid != null && !isStillValid())
		{
			return null;
		}
		return await Client.PutAsync(request, content);
	}
}
