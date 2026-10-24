using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Utility.Communication;

public class RemoteBucketCache
{
	public string RemoteServer { get; set; }

	public string RemoteBucket { get; set; }

	public Dictionary<string, RemoteKeyCacheItem> KeyCache { get; set; } = new Dictionary<string, RemoteKeyCacheItem>();

	public string Serialize()
	{
		XElement xElement = new XElement("BucketCache");
		xElement.Add(new XElement("Server", RemoteServer));
		xElement.Add(new XElement("Bucket", RemoteBucket));
		XElement content = new XElement("KeyCache");
		xElement.Add(content);
		foreach (KeyValuePair<string, RemoteKeyCacheItem> keyCache in KeyCache)
		{
			_ = keyCache;
		}
		throw new NotImplementedException();
	}

	public static RemoteBucketCache Deserialize(string xml)
	{
		throw new NotImplementedException();
	}
}
