using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Geospatial.Rasters.Classifications;
using RasMapperLib.Utilities;

namespace RasMapperLib;

public class LandCoverStack
{
	public delegate void DataChangedEventHandler();

	public delegate void AssociationChangedEventHandler();

	private string _filename;

	private string _layerName;

	private LandCoverLayer _landCover;

	private ParameterResampler _resampler;

	private InterpretationOverrideLayer _parameterOverridePolys;

	public InterpretationOverrideLayer ParameterOverrides
	{
		get => _parameterOverridePolys;
		set
		{
			if (ReferenceEquals(_parameterOverridePolys, value)) return;
			if (_parameterOverridePolys != null) _parameterOverridePolys.OnDataChanged -= OnDataChanged;
			_parameterOverridePolys = value;
			if (_parameterOverridePolys != null) _parameterOverridePolys.OnDataChanged += OnDataChanged;
			OnDataChanged();
		}
	}

	public string Filename => _filename;

	public string FileDateString
	{
		get
		{
			if (File.Exists(_filename))
			{
				return DSS.ConvertToDSSDateTime(File.GetLastWriteTime(_filename));
			}
			return "";
		}
	}

	public LandCoverLayer Layer
	{
		get
		{
			if (_landCover == null)
			{
				_landCover = TryFindOrCreateLayer(_filename, SharedData.RasMapper.MapLayersGroup, [SpecialName] () => new LandCoverLayer(System.IO.Path.GetFileNameWithoutExtension(_filename), _filename));
			}
			return _landCover;
		}
		set
		{
			if (_landCover != null)
			{
				_landCover.DataChanged -= OnDataChanged;
			}
			_landCover = value;
			if (_landCover == null)
			{
				_filename = "";
				_layerName = "";
			}
			else
			{
				_filename = _landCover.SourceFilename;
				_layerName = _landCover.Name;
				_landCover.DataChanged += OnDataChanged;
			}
			AssociationChanged?.Invoke();
			OnDataChanged();
		}
	}

	public ParameterResampler LandCoverResampler
	{
		get
		{
			if (_resampler == null)
			{
				if (Layer == null)
				{
					return null;
				}
				IClassificationResampler resampler = Layer.Resampler;
				if (resampler == null)
				{
					return null;
				}
				List<ParameterizedPolygon> overridePolygonMap = _parameterOverridePolys?.GetOverridePolygonMap() ?? new List<ParameterizedPolygon>();
				_resampler = new ParameterResampler(resampler, _parameterOverridePolys?.BaseInterpretationMergedWithLC ?? Layer.Parameters, overridePolygonMap);
			}
			return _resampler;
		}
	}

	public event DataChangedEventHandler DataChanged;

	public event AssociationChangedEventHandler AssociationChanged;

	public LandCoverStack()
	{
		_landCover = null;
	}

	private void OnDataChanged()
	{
		_resampler = null;
		DataChanged?.Invoke();
	}

	private static T TryFindOrCreateLayer<T>(string fn, BaseGroup baseGroup, Func<T> construct) where T : Layer
	{
		if (File.Exists(fn))
		{
			if (baseGroup != null)
			{
				if (!(baseGroup.FindLayerBySourceFilename(fn) is T result))
				{
					T construct2 = construct();
					baseGroup.Nodes.Add(construct2);
					return construct2;
				}
				return result;
			}
			return construct();
		}
		return null;
	}
}
