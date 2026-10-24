using System;
using System.Collections.Generic;
using System.Text;
using HDF.PInvoke;

namespace H5Assist.Identifiers;

internal class H5GroupID : IDisposable
{
	public long GroupId;

	public string GroupName;

	public bool OpenedSuccessfully => GroupId >= 0;

	public static H5GroupID Open(long FileId, string groupName)
	{
		return new H5GroupID
		{
			GroupName = groupName,
			GroupId = H5G.open(FileId, groupName, 0L)
		};
	}

	public static H5GroupID Create(long FileId, string groupName)
	{
		return new H5GroupID
		{
			GroupName = groupName,
			GroupId = H5G.create(FileId, groupName, 0L, 0L, 0L)
		};
	}

	public int GetLinkCount(H5O.type_t linkType)
	{
		H5G.info_t ginfo = default;
		H5G.get_info(GroupId, ref ginfo);
		ulong nlinks = ginfo.nlinks;
		int linkCount = 0;
		for (ulong ordinal = 0uL; ordinal < nlinks; ordinal++)
		{
			H5O.info_t oinfo = default;
			H5O.get_info_by_idx(GroupId, H5Reader.CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, ordinal, ref oinfo, 0L);
			if (oinfo.type == linkType)
			{
				linkCount++;
			}
		}
		return linkCount;
	}

	public List<string> GetLinkNames(H5O.type_t linkType)
	{
		H5G.info_t ginfo = default;
		H5G.get_info(GroupId, ref ginfo);
		ulong nlinks = ginfo.nlinks;
		Encoding utf8 = Encoding.UTF8;
		List<string> linkNames = new List<string>();
		byte[] byteValues = new byte[20];
		for (ulong ordinal = 0uL; ordinal < nlinks; ordinal++)
		{
			H5O.info_t oinfo = default;
			H5O.get_info_by_idx(GroupId, H5Reader.CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, ordinal, ref oinfo, 0L);
			if (oinfo.type == linkType)
			{
				int get_name_by_idx = (int)H5L.get_name_by_idx(GroupId, H5Reader.CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, ordinal, byteValues, byteValues.Length, 0L);
				get_name_by_idx++;
				if (get_name_by_idx > byteValues.Length)
				{
					byteValues = new byte[get_name_by_idx];
					H5L.get_name_by_idx(GroupId, H5Reader.CURRENT_LOCATION_B, H5.index_t.NAME, H5.iter_order_t.NATIVE, ordinal, byteValues, byteValues.Length, 0L);
				}
				linkNames.Add(utf8.GetString(byteValues, 0, get_name_by_idx).Trim(Convert.ToChar(0)));
			}
		}
		return linkNames;
	}

	public void Dispose()
	{
		H5G.close(GroupId);
	}
}
