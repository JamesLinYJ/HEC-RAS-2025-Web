using System;
using System.Xml.Linq;
using Utility.Units;

namespace Utility.Communication;

public class RemoteKeyCacheItem
{
	public FileProxy RemoteFile { get; set; }

	public DateTime S3LastModified { get; set; }

	public XElement ToXElement()
	{
		XElement xElement = new XElement("KeyCache");
		xElement.Add(new XElement("LastUpdate", Time.ConvertToUnixTime(S3LastModified)));
		return xElement;
	}
}
