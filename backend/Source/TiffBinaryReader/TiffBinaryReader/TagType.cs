using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TiffBinaryReader;

[DebuggerDisplay("{Name}")]
public class TagType
{
	public string Name { get; private set; }

	public ushort ID { get; private set; }

	public List<DataType> DataTypesSupported { get; private set; }

	public string Description { get; private set; }

	public ulong MaxAllowableValues { get; private set; }

	public static TagType CreateNew(string name, TagValues tv, DataType dataType, string desc, ulong maxAllowedVals)
	{
		return new TagType(name, tv, dataType, desc, maxAllowedVals);
	}

	public static TagType CreateNew(string name, TagValues tv, IList<DataType> dataTypes, string desc, ulong maxAllowedVals)
	{
		return new TagType(name, tv, dataTypes, desc, maxAllowedVals);
	}

	protected TagType(string name, TagValues tv, IList<DataType> dataTypes, string desc, ulong maxAllowedVals)
	{
		Name = name;
		ID = Convert.ToUInt16(tv);
		DataTypesSupported = dataTypes.ToList();
		Description = desc;
		MaxAllowableValues = maxAllowedVals;
		TagTypes.Register(this);
	}

	protected TagType(string name, TagValues tv, DataType dataType, string desc, ulong maxAllowedVals)
		: this(name, tv, new DataType[1] { dataType }, desc, maxAllowedVals)
	{
	}
}
