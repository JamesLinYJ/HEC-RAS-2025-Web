using System.Diagnostics;
using System.Xml.Serialization;

namespace Utility.DGML;

[DebuggerDisplay("{Source} -> {Target}")]
[XmlRoot(Namespace = "DGML")]
public class Link
{
	[XmlAttribute]
	public string Source;

	[XmlAttribute]
	public string Target;

	[XmlAttribute]
	public string Label;

	public Link(string source, string target, string label)
	{
		Source = source;
		Target = target;
		Label = label;
	}

	public Link()
	{
	}
}
