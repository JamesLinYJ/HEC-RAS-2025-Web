using System;
using System.IO;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;

namespace RasMapperLib;

public class SavedProfileInfo : IProfileInfo
{
	private string _filename;

	private string _valuesDataset;

	private int _pfStartIndex;

	private float _firstStation;

	private float _lastStation;

	private float _stationOffset;

	public int Count { get; }

	public float FirstStation => _firstStation + _stationOffset;

	public float LastStation => _lastStation + _stationOffset;

	public bool ShouldAdjustToZeroStationing => true;

	public SavedProfileInfo(int startIndex, int count, float firstStation, float lastStation, string filename, string valuesDataset)
	{
		_stationOffset = 0f;
		_pfStartIndex = startIndex;
		Count = count;
		_firstStation = firstStation;
		_lastStation = lastStation;
		_filename = filename;
		_valuesDataset = valuesDataset;
	}

	public void SetStationingOffset(float offsetValue)
	{
		_stationOffset = offsetValue;
	}

	void IProfileInfo.SetStationingOffset(float offsetValue)
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetStationingOffset
		this.SetStationingOffset(offsetValue);
	}

	public Profile GetProfile()
	{
		Profile profile = new Profile(SharedData.LengthUnits, SharedData.LengthUnits);
		Profile result;
		if (Count == 0)
		{
			result = profile;
		}
		else if (!File.Exists(_filename))
		{
			result = profile;
		}
		else
		{
			try
			{
				using (H5Reader h5Reader = new H5Reader(_filename))
				{
					float[,] values = new float[1, 1];
					string valuesDataset = _valuesDataset;
					if (h5Reader.ExistsAsDataset(valuesDataset))
					{
						h5Reader.ReadRows(valuesDataset, _pfStartIndex, Count, ref values);
						profile.AddRange(values, 0, 1);
					}
				}
				if (_stationOffset != 0f)
				{
					profile.AdjustStationing(_stationOffset);
				}
				result = profile;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = null;
				ProjectData.ClearProjectError();
			}
		}
		return result;
	}

	Profile IProfileInfo.GetProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in GetProfile
		return this.GetProfile();
	}

	public override string ToString()
	{
		return "(" + Count + " Points)";
	}
}
