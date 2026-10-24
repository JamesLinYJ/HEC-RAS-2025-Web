using System.Collections.Generic;

namespace TiffBinaryReader;

public class PhotometricTagType : TagType
{
	public PhotometricTagType(string name, TagValues tv, IList<DataType> dataTypes, string desc, ulong maxAllowedVals)
		: base(name, tv, dataTypes, desc, maxAllowedVals)
	{
	}

	public PhotometricTagType(string name, TagValues tv, DataType dataType, string desc, ulong maxAllowedVals)
		: base(name, tv, new DataType[1] { dataType }, desc, maxAllowedVals)
	{
	}
}
