using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Geospatial.Rasters.Classifications;
using H5Assist.VB;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class InterpretationRasterizerLayer : InterpolatedLayer, IAnimatingLayer
{
	private InterpretationOverrideLayer _override;

	private string _initialSelectedParameter;

	private bool _setInitialSelectedParameter;

	private int _pfIdx;

	private Func<ParameterResampler> _getRes;

	public override Extent Extent
	{
		get
		{
			ParameterResampler parameterResampler = _getRes();
			if (parameterResampler == null)
			{
				return null;
			}
			return Converter.Convert(parameterResampler.ValidExtent);
		}
	}

	public bool CanProvideMinMax { get; }

	public bool CanAnimate { get; }

	public bool IsProfile
	{
		get
		{
			ParameterResampler parameterResampler = _getRes();
			if (parameterResampler == null)
			{
				return false;
			}
			return _pfIdx >= 0 && _pfIdx < parameterResampler.ProfileCount;
		}
	}

	public bool IsMinProfile { get; }

	public bool IsMaxProfile { get; }

	public bool IsNullProfile => !IsProfile;

	public int CurrentProfileIndex
	{
		get
		{
			if (_initialSelectedParameter != null && !_setInitialSelectedParameter)
			{
				SetInitialSelectedParameter();
				_setInitialSelectedParameter = true;
			}
			return _pfIdx;
		}
		set
		{
			_pfIdx = value;
			ResetBitmap = true;
		}
	}

	public string CurrentProfileName
	{
		get
		{
			if (IsNullProfile)
			{
				return "(None)";
			}
			ParameterResampler parameterResampler = _getRes();
			if (parameterResampler == null)
			{
				return "(None)";
			}
			return parameterResampler.BaseVariables[_pfIdx].VariableName;
		}
	}

	public IList<string> AvailableProfiles => _getRes()?.ProfileNames;

	public InterpretationRasterizerLayer(string lyrName, Func<ParameterResampler> getRes, InterpretationOverrideLayer overrideLayer, string initialSelectedParameter = null)
		: base(lyrName)
	{
		_initialSelectedParameter = null;
		_setInitialSelectedParameter = false;
		_pfIdx = 0;
		CanProvideMinMax = false;
		CanAnimate = true;
		IsMinProfile = false;
		IsMaxProfile = false;
		_getRes = getRes;
		_override = overrideLayer;
		_initialSelectedParameter = initialSelectedParameter;
	}

	public void NotifyDataChanged()
	{
		ResetBitmap = true;
		if (IsNullProfile)
		{
			_pfIdx = 0;
		}
	}

	private void SetInitialSelectedParameter()
	{
		if (_initialSelectedParameter != null)
		{
			int currentProfileIndex = AvailableProfiles.IndexOf(_initialSelectedParameter);
			if (currentProfileIndex != -1)
			{
				CurrentProfileIndex = currentProfileIndex;
			}
		}
	}

	public override bool LayerHasData(H5Reader hr = null)
	{
		if (_override != null)
		{
			return _override.ClassificationLayer != null;
		}
		return false;
	}

	public override bool Resample(RasterM raster, ref float[] values)
	{
		if (IsNullProfile)
		{
			return false;
		}
		if (_getRes() == null)
		{
			return false;
		}
		return true;
	}

	private void Export(string tiffFilename, double cellSize, string paramName)
	{
		LandCoverLayer classificationLayer = _override.ClassificationLayer;
		if (classificationLayer == null)
		{
			MessageBox.Show("No associated land-cover layer found");
			return;
		}
		if (!classificationLayer.AllSourceFilesExist())
		{
			MessageBox.Show("Associated land-cover has missing files.");
			return;
		}
		Func<float[], RasterM, float[]> getTileValues = [SpecialName] (float[] tileValues, RasterM tile) => classificationLayer.GetTileValues(tileValues, tile, paramName, _override.BaseInterpretationMergedWithLC);
		classificationLayer.Export(tiffFilename, cellSize, getTileValues);
	}

	public void TrySetProfile(string profile)
	{
	}

	void IAnimatingLayer.TrySetProfile(string profile)
	{
		//ILSpy generated this explicit interface implementation from .override directive in TrySetProfile
		this.TrySetProfile(profile);
	}

	public void SetToMaxProfile()
	{
		CurrentProfileIndex = -1;
	}

	void IAnimatingLayer.SetToMaxProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMaxProfile
		this.SetToMaxProfile();
	}

	public void SetToMinProfile()
	{
		CurrentProfileIndex = -1;
	}

	void IAnimatingLayer.SetToMinProfile()
	{
		//ILSpy generated this explicit interface implementation from .override directive in SetToMinProfile
		this.SetToMinProfile();
	}
}
