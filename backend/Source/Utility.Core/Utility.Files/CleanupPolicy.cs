using System;
using System.Xml.Linq;

namespace Utility.Files;

public class CleanupPolicy
{
	public static class XmlKeys
	{
		public const string CleanupPolicy = "CleanupPolicy";

		public const string KeepAllDays = "KeepAll";

		public const string HourlyDays = "KeepHourly";

		public const string DailyDays = "KeepDaily";
	}

	public TimeSpan KeepAll { get; set; }

	public TimeSpan KeepHourly { get; set; }

	public TimeSpan KeepDaily { get; set; }

	public TimeSpan KeepWeekly { get; set; }

	public void SaveXML(XElement parent)
	{
		XElement xElement = new XElement("CleanupPolicy");
		xElement.SetAttributeValue("KeepAll", KeepAll.TotalDays);
		xElement.SetAttributeValue("KeepHourly", KeepHourly.TotalDays);
		xElement.SetAttributeValue("KeepDaily", KeepDaily.TotalDays);
		parent.Add(xElement);
	}

	public static CleanupPolicy LoadXML(XElement el)
	{
		CleanupPolicy cleanupPolicy = new CleanupPolicy();
		if (double.TryParse(el.Attribute("KeepAll")?.Value, out var result))
		{
			cleanupPolicy.KeepAll = TimeSpan.FromDays(result);
		}
		if (double.TryParse(el.Attribute("KeepHourly")?.Value, out var result2))
		{
			cleanupPolicy.KeepHourly = TimeSpan.FromDays(result2);
		}
		if (double.TryParse(el.Attribute("KeepDaily")?.Value, out var result3))
		{
			cleanupPolicy.KeepDaily = TimeSpan.FromDays(result3);
		}
		return cleanupPolicy;
	}
}
