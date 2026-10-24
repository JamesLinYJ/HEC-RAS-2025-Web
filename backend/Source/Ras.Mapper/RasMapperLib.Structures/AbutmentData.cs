using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using H5Assist;
using H5Assist.Identifiers;
using H5Assist.VB;
using Utility.Extensions;

namespace RasMapperLib.Structures;

public class AbutmentData
{
	[CompilerGenerated]
	internal sealed class _Closure_0024__11_002D0
	{
		public H5Assist.VB.H5Writer hw;

		public string attrDset;

		public string pfValsDset;

		public _Closure_0024__11_002D0(_Closure_0024__11_002D0 other)
		{
			if (other != null)
			{
				hw = other.hw;
				attrDset = other.attrDset;
				pfValsDset = other.pfValsDset;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__0()
		{
			if (hw.ExistsAsDataset(attrDset))
			{
				hw.Delete(attrDset);
			}
			if (hw.ExistsAsDataset(pfValsDset))
			{
				hw.Delete(pfValsDset);
			}
		}
	}

	private const string AttributesDatasetName = "Abutment Attributes";

	private const string ProfilesDatasetName = "Abutment Data";

	private const string StructIDColumn = "Structure ID";

	private const string UpstreamStartColumn = "US Profile (Index)";

	private const string UpstreamCountColumn = "US Profile (Count)";

	private const string DownstreamStartColumn = "DS Profile (Index)";

	private const string DownstreamCountColumn = "DS Profile (Count)";

	public Profile UpstreamProfile;

	public Profile DownstreamProfile;

	public AbutmentData(Profile usPf, Profile dsPf)
	{
		UpstreamProfile = usPf;
		DownstreamProfile = dsPf;
	}

	public static Dictionary<int, List<AbutmentData>> ReadCollection(H5Assist.VB.H5Reader hr, string h5Group)
	{
		Dictionary<int, List<AbutmentData>> abutmentDataListByInt = new Dictionary<int, List<AbutmentData>>();
		string h5GroupTerm = h5Group + "/Abutment Attributes";
		string h5GroupTerm2 = h5Group + "/Abutment Data";
		if (!hr.ExistsAsDataset(h5GroupTerm))
		{
			return abutmentDataListByInt;
		}
		CompoundDataset compoundDataset = hr.ReadCompoundDataset(h5GroupTerm);
		int[] column = compoundDataset.GetColumn<int>("Structure ID");
		int[] column2 = compoundDataset.GetColumn<int>("US Profile (Index)");
		int[] column3 = compoundDataset.GetColumn<int>("US Profile (Count)");
		int[] column4 = compoundDataset.GetColumn<int>("DS Profile (Index)");
		int[] column5 = compoundDataset.GetColumn<int>("DS Profile (Count)");
		if (column == null || column2 == null || column3 == null || column4 == null || column5 == null)
		{
			return null;
		}
		float[,] values = null;
		List<Profile> fromInfoValues = null;
		List<Profile> fromInfoValuesList = null;
		if (hr.ExistsAsDataset(h5GroupTerm2))
		{
			hr.ReadDataset(h5GroupTerm2, ref values);
			fromInfoValues = Profile.FromInfoValues(values, column2, column3);
			fromInfoValuesList = Profile.FromInfoValues(values, column4, column5);
		}
		checked
		{
			int count = column.Length - 1;
			for (int i = 0; i <= count; i++)
			{
				int key = column[i];
				Profile usPf = null;
				Profile dsPf = null;
				if (fromInfoValues != null)
				{
					usPf = fromInfoValues[i];
				}
				if (fromInfoValuesList != null)
				{
					dsPf = fromInfoValuesList[i];
				}
				AbutmentData abutmentData = new AbutmentData(usPf, dsPf);
				List<AbutmentData> abutmentDatas = null;
				if (!abutmentDataListByInt.TryGetValue(key, out abutmentDatas))
				{
					abutmentDatas = (abutmentDataListByInt[key] = new List<AbutmentData>());
				}
				abutmentDatas.Add(abutmentData);
			}
			return abutmentDataListByInt;
		}
	}

	public static void WriteCollection(H5Assist.VB.H5Writer hw, string h5Group, List<AbutmentData>[] abutmentMap)
	{
		_Closure_0024__11_002D0 capturedLocals = default;
		_Closure_0024__11_002D0 capturedLocals2 = new _Closure_0024__11_002D0(capturedLocals);
		capturedLocals2.hw = hw;
		capturedLocals2.attrDset = h5Group + "/Abutment Attributes";
		capturedLocals2.pfValsDset = h5Group + "/Abutment Data";
		Action action = [SpecialName] () =>
		{
			if (capturedLocals2.hw.ExistsAsDataset(capturedLocals2.attrDset))
			{
				capturedLocals2.hw.Delete(capturedLocals2.attrDset);
			}
			if (capturedLocals2.hw.ExistsAsDataset(capturedLocals2.pfValsDset))
			{
				capturedLocals2.hw.Delete(capturedLocals2.pfValsDset);
			}
		};
		if (abutmentMap.Length == 0)
		{
			action();
			return;
		}
		List<AbutmentData> selectMany = abutmentMap.Where([SpecialName] (List<AbutmentData> ab) => ab != null).SelectMany([SpecialName] (List<AbutmentData> ab) => ab).ToList();
		if (selectMany.Count == 0)
		{
			action();
			return;
		}
		if (!capturedLocals2.hw.ExistsAsGroup(h5Group))
		{
			capturedLocals2.hw.CreateGroup(h5Group);
		}
		List<Profile> profiles = selectMany.Select([SpecialName] (AbutmentData ab) => ab.UpstreamProfile).ToList();
		float[,] ptsXY = null;
		int[] starts = null;
		int[] counts = null;
		Profile.ToInfoValues(profiles, ref starts, ref counts, ref ptsXY);
		List<Profile> profiles2 = selectMany.Select([SpecialName] (AbutmentData ab) => ab.DownstreamProfile).ToList();
		float[,] ptsXY2 = null;
		int[] starts2 = null;
		int[] counts2 = null;
		Profile.ToInfoValues(profiles2, ref starts2, ref counts2, ref ptsXY2);
		int startsCountsTerm = 0;
		checked
		{
			if (starts.Length > 0)
			{
				startsCountsTerm = starts.Last() + counts.Last();
			}
			int count = starts2.Count() - 1;
			for (int intervalIndex = 0; intervalIndex <= count; intervalIndex++)
			{
				starts2[intervalIndex] += startsCountsTerm;
			}
			int length = ptsXY.GetLength(0);
			int length2 = ptsXY2.GetLength(0);
			float[,] floatValues = new float[length + length2 - 1 + 1, 2];
			int count2 = length - 1;
			for (int intervalIndex2 = 0; intervalIndex2 <= count2; intervalIndex2++)
			{
				floatValues[intervalIndex2, 0] = ptsXY[intervalIndex2, 0];
				floatValues[intervalIndex2, 1] = ptsXY[intervalIndex2, 1];
			}
			int count3 = length2 - 1;
			for (int intervalIndex3 = 0; intervalIndex3 <= count3; intervalIndex3++)
			{
				floatValues[intervalIndex3 + length, 0] = ptsXY2[intervalIndex3, 0];
				floatValues[intervalIndex3 + length, 1] = ptsXY2[intervalIndex3, 1];
			}
			if (floatValues.GetLength(0) > 0)
			{
				capturedLocals2.hw.WriteArray(capturedLocals2.pfValsDset, floatValues);
			}
			CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
			compoundTypeDefinition.AddField<int>("Structure ID");
			compoundTypeDefinition.AddField<int>("US Profile (Index)");
			compoundTypeDefinition.AddField<int>("US Profile (Count)");
			compoundTypeDefinition.AddField<int>("DS Profile (Index)");
			compoundTypeDefinition.AddField<int>("DS Profile (Count)");
			capturedLocals2.hw.Create1DCompoundDataset(capturedLocals2.attrDset, 10, compoundTypeDefinition);
			int ordinal = 0;
			int count4 = abutmentMap.Length - 1;
			for (int intervalIndex4 = 0; intervalIndex4 <= count4; intervalIndex4++)
			{
				List<AbutmentData> abutmentMapList = abutmentMap[intervalIndex4];
				if (!abutmentMapList.IsNullOrEmpty())
				{
					int count5 = abutmentMapList.Count - 1;
					for (int intervalIndex5 = 0; intervalIndex5 <= count5; intervalIndex5++)
					{
						_ = abutmentMapList[intervalIndex5];
						capturedLocals2.hw.AddCompoundElement(capturedLocals2.attrDset, new object[5]
						{
							intervalIndex4,
							starts[ordinal],
							counts[ordinal],
							starts2[ordinal],
							counts2[ordinal]
						});
						ordinal++;
					}
				}
			}
		}
	}
}
