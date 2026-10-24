using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Geospatial.Rasters.Classifications;
using H5Assist.Identifiers;
using H5Assist.VB;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class InterpretationOverrideLayer : PolygonFeatureLayer, IGeometryLayer
{
	public delegate void OnDataChangedEventHandler();

	[CompilerGenerated]
private sealed class _Closure_0024__42_002D0
	{
		public string varName;

		public _Closure_0024__42_002D0(_Closure_0024__42_002D0 other)
		{
			if (other != null)
			{
				varName = other.varName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Parameter ccv)
		{
			return ccv.VariableName.Equals(varName, StringComparison.OrdinalIgnoreCase);
		}
	}

	private const string LCColumnName = "Land Cover Name";

	private const string sParameterTable = "Parameter Table";

	internal ParameterSet _baseVariableOverrides;

	public List<string> IgnoreParams;

	private readonly string HDFGroupName;

	private Func<LandCoverLayer> _getLC;

	public override bool FeaturesRequireUniqueNames { get; set; }

	public override bool ForwardEditRequestsToParent { get; }

	public ParameterSet BaseInterpretationMergedWithLC => ClassificationLayer?.Parameters.OverrideWith(_baseVariableOverrides);

	public string BaseOverrideDataset => HDFGroupName + "/Base Overrides";

	public string VariableFolder => HDFGroupName + "/Variables";

	public LandCoverLayer ClassificationLayer => _getLC();

	public RASGeometry Geometry { get; }

	public override Extent Extent
	{
		get
		{
			LandCoverLayer classificationLayer = ClassificationLayer;
			Extent extent = base.Extent;
			if (classificationLayer == null || classificationLayer.Extent == null)
			{
				return extent;
			}
			if (extent == null)
			{
				return classificationLayer.Extent;
			}
			return extent.UnionXY(classificationLayer.Extent);
		}
	}
string IGeometryLayer.HDFGroupName=> HDFGroupName;

	public event OnDataChangedEventHandler OnDataChanged;

	public InterpretationOverrideLayer(RASGeometry geo, string lyrName, string geoSubFolder, Func<LandCoverLayer> getLC, List<string> ignoreParams = null, ParameterSet baseVariableOverrides = null)
		: base(lyrName, geo.SourceFilename)
	{
		FeaturesRequireUniqueNames = true;
		ForwardEditRequestsToParent = true;
		Geometry = geo;
		if (ignoreParams == null)
		{
			ignoreParams = new List<string>();
		}
		IgnoreParams = ignoreParams;
		HDFGroupName = "Geometry/" + geoSubFolder;
		if (baseVariableOverrides == null)
		{
			_baseVariableOverrides = new ParameterSet();
		}
		else
		{
			_baseVariableOverrides = baseVariableOverrides;
		}
		_getLC = getLC;
	}

	protected override void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		base.OnFeatureChanged(args);
		if (args.ChangeType == FeatureChangedType.FeatureAdded)
		{
			if (_baseVariableOverrides.Count == 0 && ClassificationLayer != null)
			{
				_baseVariableOverrides = LandCoverLayer.CopyVariableNamesFromParameterSet(ClassificationLayer.Parameters);
			}
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				SetParameterTable(featureIndicesChanged, MakeNewVariableList());
				SetupNewPolygon(featureIndicesChanged);
			}
		}
		DataChanged();
	}

	private void SetupNewPolygon(int fID)
	{
		SetUniqueFeatureName(fID);
		SetIsDefaultName(fID, isDefaultName: true);
	}

	internal void DataChanged()
	{
		OnDataChanged?.Invoke();
	}

	private ParameterSet MakeNewVariableList()
	{
		ParameterSet parameterSet = new ParameterSet();
		checked
		{
			int count = _baseVariableOverrides.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				parameterSet.Add(new Parameter(_baseVariableOverrides[i].VariableName, new Dictionary<string, float>()));
			}
			return parameterSet;
		}
	}

	public List<ParameterizedPolygon> GetOverridePolygonMap()
	{
		List<ParameterizedPolygon> parameterizedPolygons = new List<ParameterizedPolygon>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				parameterizedPolygons.Add(new ParameterizedPolygon(Converter.Convert(Polygon(i)), GetParameterTable(i)));
			}
			return parameterizedPolygons;
		}
	}

	protected override void LoadDefaultAttributeColumns()
	{
		base.LoadDefaultAttributeColumns();
		AddManagedAttributeColumn("Name", "Name", typeof(string));
		AddAttributeColumn("Parameter Table", typeof(ParameterSet));
		AddAttributeColumn("IsDefaultName", typeof(bool), null, DataColumnViewState.Invisible);
	}

	public void HDFLoadFeatureTable(H5Reader hr)
	{
		if (!hr.ExistsAsGroup(HDFGroupName))
		{
			return;
		}
		HDF5Storage.ReadPolyFeatures(this, hr, HDFGroupName + "/Polygon");
		EnsurePositivePolygonOrientations();
		string hdfGroupNameTerm = HDFGroupName + "/Attributes";
		if (hr.ExistsAsDataset(hdfGroupNameTerm))
		{
			ReadManagedColumns(hr, hdfGroupNameTerm);
		}
		checked
		{
			if (hr.ExistsAsDataset(BaseOverrideDataset))
			{
				List<string> stringList = new List<string>();
				Dictionary<string, float[]> floatValuesByString = new Dictionary<string, float[]>();
				Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements(BaseOverrideDataset);
				foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
				{
					if (compoundElement.Key.Name.RoughlyEquals("Land Cover Name"))
					{
						stringList.AddRange(compoundElement.Value as string[]);
					}
					else
					{
						floatValuesByString.Add(compoundElement.Key.Name, compoundElement.Value as float[]);
					}
				}
				_baseVariableOverrides = new ParameterSet();
				if (stringList != null && stringList.Count > 0)
				{
					foreach (KeyValuePair<string, float[]> localValue in floatValuesByString)
					{
						Parameter parameter = new Parameter(localValue.Key, new Dictionary<string, float>());
						int count = stringList.Count - 1;
						for (int i = 0; i <= count; i++)
						{
							string cls = stringList[i];
							float computed = localValue.Value[i];
							if (computed != -9999f)
							{
								parameter.AddOverride(cls, computed);
							}
						}
						_baseVariableOverrides.Add(parameter);
					}
				}
			}
			int count2 = FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				SetParameterTable(j, new ParameterSet());
			}
			string variableFolder = VariableFolder;
			if (!hr.ExistsAsGroup(variableFolder))
			{
				return;
			}
			foreach (string datasetName2 in hr.GetDatasetNames(variableFolder))
			{
				Dictionary<string, float[]> floatValuesByStringArray = new Dictionary<string, float[]>();
				string datasetName = variableFolder + "/" + datasetName2;
				Dictionary<CompoundFieldDefinition, Array> compoundElements2 = hr.ReadCompoundElements(datasetName);
				foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement2 in compoundElements2)
				{
					string name = compoundElement2.Key.Name;
					float[] floatValues = compoundElement2.Value as float[];
					if (!string.IsNullOrEmpty(name) && floatValues != null && floatValues.Length == FeatureCount())
					{
						floatValuesByStringArray[name] = floatValues;
					}
				}
				int count3 = FeatureCount() - 1;
				for (int k = 0; k <= count3; k++)
				{
					ParameterSet parameterTable = GetParameterTable(k);
					Parameter parameter2 = new Parameter(datasetName2);
					foreach (KeyValuePair<string, float[]> localValueArray in floatValuesByStringArray)
					{
						string key = localValueArray.Key;
						float computed2 = localValueArray.Value[k];
						if (!IsVariableNoData(computed2))
						{
							parameter2.AddOverride(key, computed2);
						}
					}
					parameterTable.Add(parameter2);
				}
			}
		}
	}

	void IGeometryLayer.HDFLoadFeatureTable(H5Reader hr)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFLoadFeatureTable
		this.HDFLoadFeatureTable(hr);
	}

	private static bool IsVariableNoData(float computed)
	{
		return computed == -9999f;
	}

	public void HDFSaveFeatureTable(H5Writer hw)
	{
		WriteBaseOverrides(hw);
		if (FeatureCount() == 0)
		{
			HDF5Storage.DeletePolyDataset(hw, HDFGroupName + "/Polygon");
			hw.Delete(HDFGroupName + "/Attributes");
			hw.Delete(VariableFolder);
			return;
		}
		hw.CreateGroup(HDFGroupName);
		HDF5Storage.WritePolygonFeatures(this, hw, HDFGroupName + "/Polygon");
		WriteManagedColumns(hw, HDFGroupName + "/Attributes");
		List<string> mappedLandCoverNames = GetMappedLandCoverNames();
		hw.Delete(VariableFolder);
		hw.CreateGroup(VariableFolder);
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		checked
		{
			int count = mappedLandCoverNames.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				compoundTypeDefinition.AddField<float>(mappedLandCoverNames[i]);
			}
			_Closure_0024__42_002D0 capturedLocals = default;
			foreach (Parameter baseInterpretationMergedWithLC in BaseInterpretationMergedWithLC)
			{
				capturedLocals = new _Closure_0024__42_002D0(capturedLocals);
				capturedLocals.varName = baseInterpretationMergedWithLC.VariableName;
				string datasetName = VariableFolder + "/" + capturedLocals.varName;
				hw.Create1DCompoundDataset(datasetName, 100, compoundTypeDefinition);
				int count2 = FeatureCount() - 1;
				for (int j = 0; j <= count2; j++)
				{
					Parameter parameter = GetParameterTable(j).FirstOrDefault(capturedLocals._Lambda_0024__0);
					object[] objectValues = new object[mappedLandCoverNames.Count - 1 + 1];
					if (parameter == null)
					{
						int count3 = mappedLandCoverNames.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							objectValues[k] = -9999f;
						}
					}
					else
					{
						int count4 = mappedLandCoverNames.Count - 1;
						for (int l = 0; l <= count4; l++)
						{
							if (parameter.Values.TryGetValue(mappedLandCoverNames[l], out var localValue))
							{
								objectValues[l] = localValue;
							}
							else
							{
								objectValues[l] = -9999f;
							}
						}
					}
					hw.AddCompoundElement(datasetName, objectValues);
				}
			}
		}
	}

	void IGeometryLayer.HDFSaveFeatureTable(H5Writer hw)
	{
		//ILSpy generated this explicit interface implementation from .override directive in HDFSaveFeatureTable
		this.HDFSaveFeatureTable(hw);
	}

	private List<string> GetMappedLandCoverNames()
	{
		List<string> mappedLandCoverNames = new List<string>();
		LandCoverLayer classificationLayer = ClassificationLayer;
		if (classificationLayer == null)
		{
			return mappedLandCoverNames;
		}
		mappedLandCoverNames.AddRange(classificationLayer.Classification.Classes);
		return mappedLandCoverNames;
	}

	private void WriteBaseOverrides(H5Writer hw)
	{
		hw.Delete(BaseOverrideDataset);
		if (_baseVariableOverrides.Count <= 0)
		{
			return;
		}
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		List<string> mappedLandCoverNames = GetMappedLandCoverNames();
		if (mappedLandCoverNames.Count == 0)
		{
			return;
		}
		compoundTypeDefinition.AddStringField("Land Cover Name", mappedLandCoverNames.Max([SpecialName] (string vn) => vn.Length));
		foreach (Parameter baseVariableOverride in _baseVariableOverrides)
		{
			compoundTypeDefinition.AddField<float>(baseVariableOverride.VariableName);
		}
		hw.Create1DCompoundDataset(BaseOverrideDataset, 100, compoundTypeDefinition);
		checked
		{
			int count2 = mappedLandCoverNames.Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				object[] objectValues = new object[_baseVariableOverrides.Count + 1];
				string key = (string)(objectValues[0] = mappedLandCoverNames[intervalIndex]);
				int count = _baseVariableOverrides.Count;
				for (int unity = 1; unity <= count; unity++)
				{
					if (_baseVariableOverrides[unity - 1].Values.TryGetValue(key, out var localValue))
					{
						objectValues[unity] = localValue;
					}
					else
					{
						objectValues[unity] = -9999f;
					}
				}
				hw.AddCompoundElement(BaseOverrideDataset, objectValues);
			}
		}
	}

	private void ExperimentalSave3D(H5Writer hw, string folder)
	{
		hw.CreateGroup(folder);
		LandCoverLayer classificationLayer = ClassificationLayer;
		LandCoverLayer classificationLayer2 = ClassificationLayer;
		if (classificationLayer == null || classificationLayer2 == null)
		{
			return;
		}
		checked
		{
			foreach (Parameter baseVariableOverride in _baseVariableOverrides)
			{
				string datasetName = folder + "/" + baseVariableOverride.VariableName;
				float[,,] floatValues = new float[FeatureCount() - 1 + 1, classificationLayer.Count - 1 + 1, classificationLayer2.Count - 1 + 1];
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					int count2 = classificationLayer.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						int count3 = classificationLayer2.Count - 1;
						for (int k = 0; k <= count3; k++)
						{
							floatValues[i, j, k] = (float)(i * 1000 + j * 10) + (float)k * 0.1f;
						}
					}
				}
				hw.WriteArray(datasetName, floatValues);
			}
		}
	}

	public override void MD5HashAppend(Hash hash)
	{
		base.MD5HashAppend(hash);
		HashAppend(hash, _baseVariableOverrides);
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				ParameterSet parameterTable = GetParameterTable(i);
				HashAppend(hash, parameterTable);
			}
		}
	}

	private static void HashAppend(Hash hash, ParameterSet vars)
	{
		if (vars == null)
		{
			return;
		}
		checked
		{
			int count = vars.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Parameter parameter = vars[i];
				hash.Add(parameter.VariableName);
				foreach (KeyValuePair<string, float> orderBy in parameter.Values.OrderBy([SpecialName] (KeyValuePair<string, float> kk) => kk.Key))
				{
					hash.Add(orderBy.Key);
					hash.Add(new float[1] { orderBy.Value });
				}
			}
		}
	}

	public void SetParameterTable(int fid, ParameterSet parameterTable)
	{
		FeatureRow(fid)["Parameter Table"] = parameterTable;
	}

	public ParameterSet GetParameterTable(int fid)
	{
		return FeatureRow(fid)["Parameter Table"] as ParameterSet;
	}

	public bool GetIsDefaultName(int fid)
	{
		return DataTableExtensions.GetBoolean(FeatureTable(), fid, "IsDefaultName");
	}

	public void SetIsDefaultName(int fid, bool isDefaultName)
	{
		FeatureRow(fid)["IsDefaultName"] = isDefaultName;
	}
}
