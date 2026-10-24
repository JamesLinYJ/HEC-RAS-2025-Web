using System.Collections.Generic;
using System.IO;
using H5Assist.VB;

namespace RasMapperLib.Render;

public class Names
{
	public string Max1D;

	public int Max1DRow;

	public string MaxSA;

	public int MaxSARow;

	public List<string> Max2D;

	public int Max2DRow;

	public string Min1D;

	public int Min1DRow;

	public string MinSA;

	public int MinSARow;

	public List<string> Min2D;

	public int Min2DRow;

	public string Profile1D;

	public string ProfileSA;

	public List<string> Profile2D;

	public void Get1D(int pfIndex, ref string dataset, ref int row)
	{
		switch (pfIndex)
		{
		case int.MaxValue:
			dataset = Max1D;
			row = Max1DRow;
			break;
		case int.MinValue:
			dataset = Min1D;
			row = Min1DRow;
			break;
		default:
			dataset = Profile1D;
			row = pfIndex;
			break;
		}
	}

	public void GetSA(int pfIndex, ref string dataset, ref int row)
	{
		switch (pfIndex)
		{
		case int.MaxValue:
			dataset = MaxSA;
			row = MaxSARow;
			break;
		case int.MinValue:
			dataset = MinSA;
			row = MinSARow;
			break;
		default:
			dataset = ProfileSA;
			row = pfIndex;
			break;
		}
	}

	public void Get2D(int pfIndex, ref List<string> datasets, ref int row)
	{
		switch (pfIndex)
		{
		case int.MaxValue:
			datasets = Max2D;
			row = Max2DRow;
			break;
		case int.MinValue:
			datasets = Min2D;
			row = Min2DRow;
			break;
		default:
			datasets = Profile2D;
			row = pfIndex;
			break;
		}
	}

	public void Validate(string filename)
	{
		if (File.Exists(filename))
		{
			using (H5Reader h5r = new H5Reader(filename))
			{
				ValidateInternal(h5r, ref Max1D);
				ValidateInternal(h5r, ref MaxSA);
				ValidateInternal(h5r, ref Max2D);
				ValidateInternal(h5r, ref Min1D);
				ValidateInternal(h5r, ref MinSA);
				ValidateInternal(h5r, ref Min2D);
				ValidateInternal(h5r, ref Profile1D);
				ValidateInternal(h5r, ref ProfileSA);
				ValidateInternal(h5r, ref Profile2D);
				return;
			}
		}
		Max1D = null;
		MaxSA = null;
		Max2D = null;
		Min1D = null;
		MinSA = null;
		Min2D = null;
		Profile1D = null;
		ProfileSA = null;
		Profile2D = null;
	}

	private static void ValidateInternal(H5Reader h5r, ref string dset)
	{
		if (dset != null && !h5r.ExistsAsDataset(dset))
		{
			dset = null;
		}
	}

	private static void ValidateInternal(H5Reader h5r, ref List<string> dsets)
	{
		if (dsets == null)
		{
			return;
		}
		bool condition = false;
		foreach (string dset in dsets)
		{
			if (!h5r.ExistsAsDataset(dset))
			{
				condition = true;
				break;
			}
		}
		if (condition)
		{
			dsets.Clear();
		}
	}
}
