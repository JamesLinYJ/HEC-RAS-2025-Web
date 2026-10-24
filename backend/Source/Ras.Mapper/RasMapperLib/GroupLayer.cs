using System.Runtime.CompilerServices;
using System.Xml;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class GroupLayer : Layer
{
	[CompilerGenerated]
	private bool _CanEdit;

	[CompilerGenerated]
	private bool _CanRemove;

	[CompilerGenerated]
	private bool _CanRename;

	public override bool CanEdit
	{
		[CompilerGenerated]
		get
		{
			return _CanEdit;
		}
	}

	public override bool CanRemove
	{
		[CompilerGenerated]
		get
		{
			return _CanRemove;
		}
	}

	public override bool CanRename
	{
		[CompilerGenerated]
		get
		{
			return _CanRename;
		}
	}

	public GroupLayer()
		: this("")
	{
	}

	public GroupLayer(string lyrName, bool canRemove = false, bool canRename = false)
		: base(lyrName)
	{
		_CanEdit = false;
		_CanRemove = false;
		_CanRename = false;
		_CanRemove = canRemove;
		_CanRename = canRename;
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlEl)
	{
		base.XMLSaveMetadata(doc, xmlEl);
		if (CanRemove)
		{
			xmlEl.SetAttribute("CanRemove", "True");
		}
		if (CanRename)
		{
			xmlEl.SetAttribute("CanRename", "True");
		}
		if (CanEdit)
		{
			xmlEl.SetAttribute("CanEdit", "True");
		}
	}

	protected override void XMLLoadMetadata(XmlElement xmlNode)
	{
		base.XMLLoadMetadata(xmlNode);
		_CanRemove = XML.BooleanAttribute(xmlNode, "CanRemove");
		_CanRename = XML.BooleanAttribute(xmlNode, "CanRename");
		_CanEdit = XML.BooleanAttribute(xmlNode, "CanEdit");
	}

	public void XMLLoadJustMetadata(XmlElement xmlel)
	{
		XMLLoadMetadata(xmlel);
	}
}
