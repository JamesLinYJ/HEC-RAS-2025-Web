using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.Rasters.Classifications;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility;
using Utility.Extensions;

namespace RasMapperLib;

public class RasLandCoverRegions : ManningsNPolygonLayer, IGeometryLayer
{
	[CompilerGenerated]
private sealed class _Closure_0024__26_002D0
	{
		public string capturedName;

		public _Closure_0024__26_002D0(_Closure_0024__26_002D0 other)
		{
			if (other != null)
			{
				capturedName = other.capturedName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__2(KeyValuePair<CompoundFieldDefinition, Array> kvp)
		{
			return string.Equals(kvp.Key.Name, capturedName, StringComparison.CurrentCultureIgnoreCase);
		}
	}

	public const string sLandCoverMapping = "Land Cover Mapping";

	public const string sLandCoverRegions = "Calibration Regions";

	public const string HDFGroupName = "Geometry/Land Cover (Manning's n)";

	public const string CalibrationTableName = "Geometry/Land Cover (Manning's n)/Calibration Table";

	private string[] _baseNames;

	private float[] _baseNValues;

	public RASGeometry Geometry { get; }

	public override bool ForwardEditRequestsToParent { get; }

	public override string DefaultLayerName => "Calibration Regions";

	private string[] BaseNames
	{
		get
		{
			return _baseNames;
		}
		set
		{
			Dictionary<string, float> ordinalIgnoreCase = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
			checked
			{
				if (_baseNames != null && _baseNValues != null && _baseNames.Count() == _baseNValues.Count())
				{
					int count = _baseNames.Count() - 1;
					for (int i = 0; i <= count; i++)
					{
						ordinalIgnoreCase[_baseNames[i]] = _baseNValues[i];
					}
				}
				_baseNames = value;
				float[] baseNValues = new float[_baseNames.Length - 1 + 1];
				Buffers.ArraySetAllVB(baseNValues, float.NaN);
				int count2 = _baseNames.Length - 1;
				for (int j = 0; j <= count2; j++)
				{
					string baseName = _baseNames[j];
					if (Operators.CompareString(baseName, "NoData", TextCompare: false) == 0 && ordinalIgnoreCase.ContainsKey(""))
					{
						baseNValues[j] = ordinalIgnoreCase[""];
					}
					if (ordinalIgnoreCase.ContainsKey(baseName))
					{
						baseNValues[j] = ordinalIgnoreCase[baseName];
					}
				}
				_baseNValues = baseNValues;
			}
		}
	}
string IGeometryLayer.HDFGroupName=> "Geometry/Land Cover (Manning's n)";

	public RasLandCoverRegions(RASGeometry geometry)
		: base("Calibration Regions", geometry.SourceFilename)
	{
		ForwardEditRequestsToParent = true;
		Geometry = geometry;
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddAttributeColumn("Land Cover Mapping", typeof(Dictionary<string, float>), null, DataColumnViewState.Invisible);
		if (SharedData.DebugMode)
		{
			AddManagedAttributeColumn("n_Value", "n Value", typeof(float));
			AddManagedAttributeColumn("LC Adjustment Factor", "LC Adjustment Factor", typeof(float));
		}
	}

	public string[] GetLCNames()
	{
		EnsureBaseNamesAndValues();
		if (Geometry?.NValueLayer != null)
		{
			BaseNames = Geometry.NValueLayer.Classification.Classes.ToArray();
		}
		return _baseNames?.ToArray();
	}

	public void EnsureBaseNamesAndValues()
	{
		if (_baseNValues == null || _baseNames == null)
		{
			FeatureTable();
			return;
		}
		string[] baseNames = _baseNames;
		FeatureTable();
		_baseNames = baseNames;
	}

	public Parameter GetBaseNValueClassificationVariable()
	{
		Parameter parameter = new Parameter("ManningsN");
		string[] lCNames = GetLCNames();
		if (lCNames == null || _baseNValues == null)
		{
			return parameter;
		}
		if (lCNames.Count() != _baseNValues.Count())
		{
			throw new Exception("Name/N-value count difference");
		}
		checked
		{
			int count = _baseNValues.Count() - 1;
			for (int i = 0; i <= count; i++)
			{
				parameter.AddOverride(lCNames[i], _baseNValues[i]);
			}
			return parameter;
		}
	}

	public void SetBaseNValueFromClassificationVariable(Parameter param)
	{
		checked
		{
			_baseNames = new string[param.Values.Count - 1 + 1];
			_baseNValues = new float[param.Values.Count - 1 + 1];
			int ordinal = 0;
			foreach (KeyValuePair<string, float> localValue in param.Values)
			{
				_baseNames[ordinal] = localValue.Key;
				_baseNValues[ordinal] = localValue.Value;
				ordinal++;
			}
		}
	}

	public float[] GetOverriddenNValues()
	{
		GetLCNames();
		return _baseNValues?.ToArray();
	}

	protected void HDFLoadFeatureTable(H5Reader hr)
	{
		if (!hr.ExistsAsGroup("Geometry/Land Cover (Manning's n)"))
		{
			return;
		}
		string path = "Geometry/Land Cover (Manning's n)/Attributes";
		if (hr.ExistsAsDataset(path))
		{
			HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Land Cover (Manning's n)/Polygon", sanitizeDuplicatePoints: false, forcePolygon: true);
			ReadManagedColumns(hr, path);
		}
		else
		{
			HDF5Storage.ReadPolyFeatures(this, hr, "Geometry/Land Cover (Manning's n)/Region");
			HDFLoadFeatureTable_503(hr);
		}
		EnsurePositivePolygonOrientations();
		if (!hr.ExistsAsDataset("Geometry/Land Cover (Manning's n)/Calibration Table"))
		{
			return;
		}
		Dictionary<CompoundFieldDefinition, Array> source = hr.ReadCompoundElements("Geometry/Land Cover (Manning's n)/Calibration Table");
		Array firstOrDefaultSourceTerm = source.FirstOrDefault([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => string.Equals(kvp.Key.Name, "Land Cover Name", StringComparison.CurrentCultureIgnoreCase)).Value;
		Array firstOrDefaultSourceTerm2 = source.FirstOrDefault([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => string.Equals(kvp.Key.Name, "Base Manning's n Value", StringComparison.CurrentCultureIgnoreCase)).Value;
		if (firstOrDefaultSourceTerm == null || firstOrDefaultSourceTerm2 == null)
		{
			return;
		}
		_baseNames = firstOrDefaultSourceTerm as string[];
		_baseNValues = firstOrDefaultSourceTerm2 as float[];
		if (_baseNames == null)
		{
			return;
		}
		checked
		{
			int count = FeatureCount() - 1;
			_Closure_0024__26_002D0 capturedLocals = default;
			for (int ordinal = 0; ordinal <= count; ordinal++)
			{
				capturedLocals = new _Closure_0024__26_002D0(capturedLocals);
				capturedLocals.capturedName = GetFeatureName(ordinal);
				Array firstOrDefaultSourceTerm3 = source.FirstOrDefault(capturedLocals._Lambda_0024__2).Value;
				if (firstOrDefaultSourceTerm3 != null && firstOrDefaultSourceTerm3 is float[] floatValues)
				{
					int count2 = floatValues.Count() - 1;
					for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
					{
						AddLandCoverMapping(ordinal, _baseNames[intervalIndex], floatValues[intervalIndex]);
					}
				}
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	protected void HDFLoadFeatureTable_503(H5Reader hr)
	{
		HDF5Storage.ReadStringColumn(this, hr, "Geometry/Land Cover (Manning's n)/Region Names", "Name");
	}

	protected void HDFSaveFeatureTable(H5Writer hw)
	{
		hw.CreateGroup("Geometry/Land Cover (Manning's n)");
		string path = "Geometry/Land Cover (Manning's n)/Attributes";
		if (FeatureCount() == 0)
		{
			hw.Delete(path);
			hw.Delete("Geometry/Land Cover (Manning's n)/Polygon Info");
			hw.Delete("Geometry/Land Cover (Manning's n)/Polygon Parts");
			hw.Delete("Geometry/Land Cover (Manning's n)/Polygon Points");
			hw.Delete("Geometry/Land Cover (Manning's n)/Region Info");
			hw.Delete("Geometry/Land Cover (Manning's n)/Region Parts");
			hw.Delete("Geometry/Land Cover (Manning's n)/Region Points");
			hw.Delete("Geometry/Land Cover (Manning's n)/Region Names");
		}
		else
		{
			HDF5Storage.WritePolygonFeatures(this, hw, "Geometry/Land Cover (Manning's n)/Polygon");
			WriteManagedColumns(hw, path);
		}
		string[] lCNames = GetLCNames();
		if (lCNames == null || _baseNValues == null)
		{
			return;
		}
		if (hw.ExistsAsDataset("Geometry/Land Cover (Manning's n)/Calibration Table"))
		{
			hw.Delete("Geometry/Land Cover (Manning's n)/Calibration Table");
		}
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		compoundTypeDefinition.AddStringField("Land Cover Name", lCNames.Max([SpecialName] (string textValue) => textValue.Length));
		compoundTypeDefinition.AddField<float>("Base Manning's n Value");
		HashSet<string> stringSet = new HashSet<string>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int ordinal = 0; ordinal <= count; ordinal++)
			{
				if (!stringSet.Contains(GetFeatureName(ordinal)))
				{
					stringSet.Add(GetFeatureName(ordinal));
					compoundTypeDefinition.AddField<float>(GetFeatureName(ordinal));
				}
			}
			hw.Create1DCompoundDataset("Geometry/Land Cover (Manning's n)/Calibration Table", lCNames.Count(), compoundTypeDefinition);
			using H5Assist.VB.CompoundWriter compoundWriter = new H5Assist.VB.CompoundWriter(hw, "Geometry/Land Cover (Manning's n)/Calibration Table", lCNames.Count());
			compoundWriter.WriteColumn(0, lCNames);
			compoundWriter.WriteColumn(1, _baseNValues);
			stringSet.Clear();
			int ordinal2 = 2;
			int count2 = FeatureCount() - 1;
			for (int ordinal3 = 0; ordinal3 <= count2; ordinal3++)
			{
				if (stringSet.Contains(GetFeatureName(ordinal3)))
				{
					continue;
				}
				stringSet.Add(GetFeatureName(ordinal3));
				Dictionary<string, float> landCoverMapping = GetLandCoverMapping(ordinal3);
				if (landCoverMapping == null)
				{
					landCoverMapping = new Dictionary<string, float>();
				}
				float manningNValue = GetManningNValue(ordinal3);
				if (manningNValue <= 0f)
				{
					manningNValue = float.NaN;
				}
				float[] floatValues = new float[lCNames.Count() - 1 + 1];
				int count3 = lCNames.Count() - 1;
				for (int intervalIndex = 0; intervalIndex <= count3; intervalIndex++)
				{
					string key = lCNames[intervalIndex];
					if (float.IsNaN(manningNValue))
					{
						float naN = float.NaN;
						landCoverMapping.TryGetValue(key, out naN);
						floatValues[intervalIndex] = naN;
					}
					else
					{
						floatValues[intervalIndex] = manningNValue;
					}
				}
				compoundWriter.WriteColumn(ordinal2, floatValues);
				ordinal2++;
			}
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType != FeatureChangedType.FeatureAdded && args.ChangeType != FeatureChangedType.FeatureModified)
		{
			return;
		}
		foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
		{
			if (string.IsNullOrEmpty(GetFeatureName(featureIndicesChanged)))
			{
				SetFeatureName(featureIndicesChanged, GetUniqueFeatureName());
			}
		}
	}

	public override void MD5HashAppend(Hash hash)
	{
		base.MD5HashAppend(hash);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Dictionary<string, float> landCoverMapping_StripNoData = GetLandCoverMapping_StripNoData(i);
				if (landCoverMapping_StripNoData == null)
				{
					continue;
				}
				foreach (KeyValuePair<string, float> orderBy in landCoverMapping_StripNoData.OrderBy([SpecialName] (KeyValuePair<string, float> a) => a.Key))
				{
					hash.Add(orderBy.Key);
					hash.Add(new float[1] { orderBy.Value });
				}
			}
			string[] lCNames = GetLCNames();
			if (lCNames != null)
			{
				int count2 = lCNames.Count() - 1;
				for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
				{
					hash.Add(lCNames[intervalIndex]);
				}
			}
			if (_baseNValues != null)
			{
				hash.Add(_baseNValues);
			}
		}
	}

	public Dictionary<string, float> GetLandCoverMapping_StripNoData(int fid)
	{
		Dictionary<string, float> landCoverMapping = GetLandCoverMapping(fid);
		if (landCoverMapping == null)
		{
			return landCoverMapping;
		}
		landCoverMapping.RemoveWhere([SpecialName] (KeyValuePair<string, float> kvp) => kvp.Value == float.MaxValue || kvp.Value == -9999f || kvp.Value == 0f || float.IsNaN(kvp.Value));
		return landCoverMapping;
	}

	public Dictionary<string, float> GetLandCoverMapping(int fid)
	{
		object objectValue = RuntimeHelpers.GetObjectValue(FeatureRow(fid)["Land Cover Mapping"]);
		if (Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) || objectValue == null)
		{
			return null;
		}
		return objectValue as Dictionary<string, float>;
	}

	public void SetLandCoverMapping(int fID, Dictionary<string, float> dic)
	{
		FeatureRow(fID)["Land Cover Mapping"] = dic;
	}

	public void AddLandCoverMapping(int fID, string lcName, float nValue)
	{
		Dictionary<string, float> landCoverMapping = GetLandCoverMapping(fID);
		if (landCoverMapping == null)
		{
			landCoverMapping = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		}
		SetLandCoverMapping(fID, landCoverMapping);
		landCoverMapping[lcName] = nValue;
	}
}
