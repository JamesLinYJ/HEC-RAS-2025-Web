using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml;
using Geospatial.GDALAssist;
using Geospatial.IO;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist.Identifiers;
using H5Assist.VB;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using TiffAssist;
using Utility;
using Utility.Extensions;
using Utility.Files;

namespace RasMapperLib;

public class LandCoverLayer : Layer
{
	public delegate void DataChangedEventHandler();

	public enum LandCoverType
	{
		LandCover,
		Soils,
		SedimentBedMaterial,
		InfiltrationDeficitConstantLoss,
		InfiltrationSCSCurveNumber,
		InfiltrationGreenAmpt
	}

	public interface ILandCoverClassificationLayer
	{
	}

	protected class LandCoverClassificationLayer : PolygonFeatureLayer, ILandCoverClassificationLayer
	{
		public const string HDFGroupName = "/Classification Polygons";

		public const string AttributeDatasetName = "/Classification Polygons/Attributes";

		public const string ClassificationColumnName = "Name";

		private readonly LandCoverLayer _parent;

		public override bool AutoUpdateOnSourceFileChange { get; }

		public override bool CanRename { get; }

		public LandCoverClassificationLayer(LandCoverLayer lcl)
			: base("Classification Polygons", lcl.HDFFilename)
		{
			AutoUpdateOnSourceFileChange = false;
			CanRename = false;
			_parent = lcl;
		}

		protected override void LoadDefaultAttributeColumns()
		{
			base.LoadDefaultAttributeColumns();
			AddManagedAttributeColumn("Name", "Classification", typeof(string));
		}

		protected override void LoadFeatureTable()
		{
			if (!File.Exists(_parent.HDFFilename))
			{
				ErrorMessage = "Source file " + SourceFilename + " does not exist.";
				return;
			}
			try
			{
				using H5Reader h5Reader = new H5Reader(_parent.HDFFilename);
				if (h5Reader.ExistsAsGroup("/Classification Polygons"))
				{
					HDF5Storage.ReadPolyFeatures(this, h5Reader, "/Classification Polygons/Polygon");
					EnsurePositivePolygonOrientations();
					if (FeatureCount() != 0 && h5Reader.ExistsAsDataset("/Classification Polygons/Attributes"))
					{
						ReadManagedColumns(h5Reader, "/Classification Polygons/Attributes");
					}
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}

		protected override bool SaveFeatureTableInternal()
		{
			using (H5Writer h5Writer = new H5Writer(_parent.HDFFilename))
			{
				if (h5Writer.ExistsAsGroup("/Classification Polygons"))
				{
					h5Writer.Delete("/Classification Polygons");
				}
				if (FeatureCount() == 0)
				{
					return true;
				}
				h5Writer.CreateGroup("/Classification Polygons");
				WriteManagedColumns(h5Writer, "/Classification Polygons/Attributes");
				HDF5Storage.WritePolygonFeatures(this, h5Writer, "/Classification Polygons/Polygon");
			}
			return true;
		}

		public string GetClassificationName(int fid)
		{
			return GetFeatureName(fid);
		}

		public override void StopEditing(bool save)
		{
			base.StopEditing(save);
			_parent.BaseDataUpdated();
		}

		public void HashAppend(Hash hash)
		{
			checked
			{
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					Polygon(i).MD5HashAppend(hash);
					hash.Add(GetClassificationName(i));
				}
			}
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__104_002D0
	{
		public string parameter;

		public _Closure_0024__104_002D0(_Closure_0024__104_002D0 other)
		{
			if (other != null)
			{
				parameter = other.parameter;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__2(Parameter p)
		{
			return Operators.CompareString(p.VariableName, parameter, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__122_002D0
	{
		public string fn;

		public _Closure_0024__122_002D0(_Closure_0024__122_002D0 other)
		{
			if (other != null)
			{
				fn = other.fn;
			}
		}

		[SpecialName]
		internal LandCoverLayer _Lambda_0024__0()
		{
			return new LandCoverLayer(fn);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__124_002D0
	{
		public string paramName;

		public _Closure_0024__124_002D0(_Closure_0024__124_002D0 other)
		{
			if (other != null)
			{
				paramName = other.paramName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Parameter paramInParameters)
		{
			return Operators.CompareString(paramInParameters.VariableName, paramName, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__126_002D0
	{
		public string hdfFilename;

		public _Closure_0024__126_002D0(_Closure_0024__126_002D0 other)
		{
			if (other != null)
			{
				hdfFilename = other.hdfFilename;
			}
		}

		[SpecialName]
		internal string _Lambda_0024__0(string fn)
		{
			return RasMapperLib.Utilities.Path.MakeAbsolute(fn, hdfFilename);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__126_002D1
	{
		public string[] newVirtualDependencyPathNames;

		public _Closure_0024__126_002D0 outerClosure;

		public _Closure_0024__126_002D1(_Closure_0024__126_002D1 other)
		{
			if (other != null)
			{
				newVirtualDependencyPathNames = other.newVirtualDependencyPathNames;
			}
		}

		[SpecialName]
		internal void _Lambda_0024__1()
		{
			using H5Writer hw = new H5Writer(outerClosure.hdfFilename);
			WriteDependencyPathNames(hw, newVirtualDependencyPathNames);
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__138_002D0
	{
		public Parameter parameter;

		public _Closure_0024__138_002D0(_Closure_0024__138_002D0 other)
		{
			if (other != null)
			{
				parameter = other.parameter;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Parameter p)
		{
			return Operators.CompareString(p.VariableName, parameter.VariableName, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__139_002D0
	{
		public string cName;

		public _Closure_0024__139_002D0(_Closure_0024__139_002D0 other)
		{
			if (other != null)
			{
				cName = other.cName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Parameter v)
		{
			return Operators.CompareString(v.VariableName, cName, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__140_002D0
	{
		public string cName;

		public _Closure_0024__140_002D0(_Closure_0024__140_002D0 other)
		{
			if (other != null)
			{
				cName = other.cName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Parameter v)
		{
			return Operators.CompareString(v.VariableName, cName, TextCompare: false) == 0;
		}
	}

	[CompilerGenerated]
	internal sealed class _Closure_0024__141_002D0
	{
		public string variableName;

		public _Closure_0024__141_002D0(_Closure_0024__141_002D0 other)
		{
			if (other != null)
			{
				variableName = other.variableName;
			}
		}

		[SpecialName]
		internal bool _Lambda_0024__0(Parameter cv)
		{
			return cv.VariableName.RoughlyEquals(variableName);
		}
	}

	public const string MANNINGS_HDF = "ManningsN";

	public const string PERCENTIMPERV_HDF = "Percent Impervious";

	public const string NAMES_HDF = "Names";

	public const string IDS_HDF = "IDs";

	public const string DTName = "Name";

	public const string DTID = "ID";

	public const string sSelectedParameterForSurfaceFillLabeltag = "SelectedParameterForSurfaceFillLabel";

	private bool _rasterExtentRead;

	private Extent _rasterExtent;

	private const string sGUIDAttribute = "GUID";

	public const int NoData = 0;

	private const float VariableNoData = -9999f;

	public const string shdfFilterSuffix = "(*.hdf)|*.hdf";

	private const string sLayerDescriptor = "Land Coverage";

	private const string sOverridePolygonLayerName = "Classification Polygons";

	private const string sHasRasterFileAttribute = "Has Raster File";

	private const string sVirtualDependencies = "Virtual Dependencies";

	private const string sVirtualDependencyTypes = "Virtual Dependency Types";

	private const string sType = "LC Type";

	public const bool UseGDALRasterization = false;

	public static readonly Version CurrentVersion = new Version(2, 0);

	private bool _isVirtualDependencyMode;

	private string[] _virtualDependencies;

	private LandCoverLayer[] _virtualLCDependencies;

	private LandCoverType[] _virtualDependencyTypes;

	private bool _hasRasterFile;

	private Classification _baseRasterClassification;

	private ParameterSet _baseParameters;

	private LandCoverClassificationLayer _overridePolygonLayer;

	private IClassification _classification;

	private IClassificationResampler _resampler;

	private string _guid;


	public const string sDEFICITCONSTANT = "Deficit Constant";

	public const string sSCS = "SCS Curve Number";

	public const string sGreenAmpt = "Green and Ampt";

	public const string sLandCoverFolder = "Land Classification";

	public const string sDefaultFilename = "LandClassification";

	private LandCoverType _landCoverType;

	private static Dictionary<LandCoverType, LandCoverLayerHelper> _landCoverTypeToHelper = new Dictionary<LandCoverType, LandCoverLayerHelper>
	{
		{
			LandCoverType.LandCover,
			LandCoverLayerHelper.ManningsN()
		},
		{
			LandCoverType.Soils,
			LandCoverLayerHelper.DefaultSoils()
		},
		{
			LandCoverType.InfiltrationDeficitConstantLoss,
			LandCoverLayerHelper.InfiltrationDC()
		},
		{
			LandCoverType.InfiltrationSCSCurveNumber,
			LandCoverLayerHelper.InfiltrationSCS()
		},
		{
			LandCoverType.InfiltrationGreenAmpt,
			LandCoverLayerHelper.InfiltrationGA()
		}
	};

	private Dictionary<string, float> _properties;

	private const string sProperty = "Name";

	private const string sValue = "Value";

	public const string sPropertySCSInitialLostResetTime = "SCS Initial Loss Reset Time";

	public const string sRasterDataset = "/Raster Map";

	public const string sVariableDataset = "/Variables";

	public const string sDependencyClassificationsDataset = "/Dependency Classifications";

	public const string BaseOverridePrefix = "Base Override - ";

	public const string sPropertiesDataset = "/Properties";

	public override bool CanRename { get; }

	public override Extent Extent
	{
		get
		{
			checked
			{
				if (VirtualDependencyMode)
				{
					LandCoverLayer[] virtualDependencies = EvaluateVirtualDependencies();
					int count = virtualDependencies.Length - 1;
					for (int i = 0; i <= count; i++)
					{
						if (virtualDependencies[i]._hasRasterFile && !virtualDependencies[i]._rasterExtentRead)
						{
							virtualDependencies[i].ReadRasterExtent();
						}
						_rasterExtent = Extent.Union(_rasterExtent, virtualDependencies[i]._rasterExtent);
					}
				}
				else if (_hasRasterFile && !_rasterExtentRead)
				{
					ReadRasterExtent();
				}
				_extent = Extent.Union(_overridePolygonLayer.Extent, _rasterExtent);
				return _extent;
			}
		}
	}

	public LandCoverType LCType => _landCoverType;

	public bool VirtualDependencyMode => _isVirtualDependencyMode;

	internal PolygonFeatureLayer OverridePolygonLayer => _overridePolygonLayer;

	public ParameterSet Parameters => _baseParameters;

	public IClassification Classification
	{
		get
		{
			if (_classification != null)
			{
				return _classification;
			}
			if (_hasRasterFile && _baseRasterClassification == null)
			{
				return null;
			}
			if (VirtualDependencyMode)
			{
				IClassification virtualClassification = GetVirtualClassification();
				if (virtualClassification != null && virtualClassification is MergedClassification)
				{
					_classification = ((MergedClassification)virtualClassification).ToClassification();
				}
				else if (virtualClassification != null)
				{
					_classification = virtualClassification;
				}
				if (_classification == null)
				{
					return null;
				}
			}
			else if (_hasRasterFile)
			{
				_classification = new Classification(_baseRasterClassification.GetAllClassifications());
			}
			else
			{
				_classification = new Classification();
			}
			List<string> classNames = GetValidClassificationNames().ToList();
			Geospatial.Rasters.Classifications.Classification.AddRangeAsAvailableIDs(_classification, classNames);
			return _classification;
		}
	}

	public byte DefaultAlphaValue => 128;

	private string HDFFilename => SourceFilename;

	public string TiffFilename => EnsureIsTiffFilename(SourceFilename);

	public int Count => Classification.Count;

	public bool HasRasterFile => _hasRasterFile;

	public virtual string LayerDescriptor { get; }

	public string LayerFileFilter => LayerDescriptor + " (*.hdf)|*.hdf";

	public IClassificationResampler Resampler
	{
		get
		{
			if (_resampler != null)
			{
				return _resampler;
			}
			if (!AllSourceFilesExist())
			{
				return null;
			}
			if (VirtualDependencyMode)
			{
				LandCoverLayer[] virtualDependencies = EvaluateVirtualDependencies();
				if (virtualDependencies == null)
				{
					return null;
				}
				if (virtualDependencies.Count() == 1)
				{
					LandCoverLayer landCoverLayer = virtualDependencies[0];
					_resampler = new ClassificationResampler(landCoverLayer.Resampler, landCoverLayer.Classification);
				}
				else if (virtualDependencies.Count() == 2)
				{
					LandCoverLayer landCoverLayer2 = virtualDependencies[0];
					LandCoverLayer landCoverLayer3 = virtualDependencies[1];
					_resampler = new MergedClassificationResampler(landCoverLayer2.Resampler, landCoverLayer3.Resampler, (MergedClassification)GetVirtualClassification());
				}
				else
				{
					ErrorMessage = "Error - Expected 1 or 2 virtual dependencies for this Land Cover";
				}
				return _resampler;
			}
			throw new NotImplementedException();
		}
	}

	public event DataChangedEventHandler DataChanged;

	public LandCoverLayer()
		: base("Land Coverage")
	{
		CanRename = false;
		_properties = new Dictionary<string, float>();
		LayerDescriptor = "Land Coverage";
		Initialize();
	}

	public LandCoverLayer(string filename, LandCoverType type = LandCoverType.LandCover)
		: base(System.IO.Path.GetFileNameWithoutExtension(filename), filename)
	{
		CanRename = false;
		_properties = new Dictionary<string, float>();
		LayerDescriptor = "Land Coverage";
		Initialize();
		_landCoverType = type;
		ReadLandCoverData();
		AssignParameters();
	}

	public LandCoverLayer(string name, string filename, LandCoverType type = LandCoverType.LandCover)
		: base(name, EnsureIsHDF5Filename(filename))
	{
		CanRename = false;
		_properties = new Dictionary<string, float>();
		LayerDescriptor = "Land Coverage";
		Initialize();
		_landCoverType = type;
		ReadLandCoverData();
		AssignParameters();
	}

	private LandCoverLayer(string filename, Classification baseRasterClass, string[] virtualDeps, LandCoverType type, Dictionary<string, float> properties = null)
		: this()
	{
		_properties = properties;
		SourceFilename = filename;
		Name = System.IO.Path.GetFileNameWithoutExtension(filename);
		_baseRasterClassification = baseRasterClass;
		_hasRasterFile = baseRasterClass != null;
		_virtualDependencies = virtualDeps;
		_isVirtualDependencyMode = virtualDeps != null;
		_baseParameters = new ParameterSet();
		_landCoverType = type;
		AssignParameters();
	}

	private void AssignParameters()
	{
		List<string> stringList = new List<string>();
		HashSet<string> stringSet = new HashSet<string>();
		checked
		{
			int count = _baseParameters.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				stringSet.Add(_baseParameters[i].VariableName);
			}
			if (_landCoverType == LandCoverType.LandCover)
			{
				if (!stringSet.Contains("ManningsN"))
				{
					stringList.Add("ManningsN");
				}
				if (!stringSet.Contains("Percent Impervious"))
				{
					stringList.Add("Percent Impervious");
				}
			}
			if (_landCoverType == LandCoverType.InfiltrationDeficitConstantLoss)
			{
				List<string> allVariables = ParameterNames.InfiltrationVariables.DeficitConstantLoss.AllVariables.ToList();
				foreach (string allVariable in allVariables)
				{
					if (!stringSet.Contains(allVariable))
					{
						stringList.Add(allVariable);
					}
				}
			}
			else if (_landCoverType == LandCoverType.InfiltrationSCSCurveNumber)
			{
				List<string> allVariablesList = ParameterNames.InfiltrationVariables.SCSCurveNumber.AllVariables.ToList();
				foreach (string allVariable2 in allVariablesList)
				{
					if (!stringSet.Contains(allVariable2))
					{
						stringList.Add(allVariable2);
					}
				}
			}
			else if (_landCoverType == LandCoverType.InfiltrationGreenAmpt)
			{
				List<string> allVariables2 = ParameterNames.InfiltrationVariables.GreenAmpt.AllVariables.ToList();
				foreach (string allVariable3 in allVariables2)
				{
					if (!stringSet.Contains(allVariable3))
					{
						stringList.Add(allVariable3);
					}
				}
			}
			if (stringList.Count <= 0)
			{
				return;
			}
			ParameterSet parameterSet = new ParameterSet();
			foreach (Parameter baseParameter in _baseParameters)
			{
				parameterSet.Add(baseParameter);
			}
			foreach (string textValue in stringList)
			{
				parameterSet.Add(new Parameter(textValue));
			}
			_baseParameters = parameterSet;
		}
	}

	private void ReadRasterExtent()
	{
		try
		{
			Geospatial.Vectors.Extent fullNativeExtents = GDALRaster.GetFullNativeExtents(System.IO.Path.ChangeExtension(SourceFilename, "tif"));
			_rasterExtent = Converter.Convert(fullNativeExtents);
			_rasterExtentRead = true;
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ProjectData.ClearProjectError();
		}
	}

	private void HandleColorValuesChanged(object sender, EventArgs e)
	{
	}

	public static LandCoverLayer CreateEmpty(string filename, LandCoverType type, Dictionary<string, float> properties = null)
	{
		LandCoverLayer landCoverLayer = new LandCoverLayer(filename, null, null, type, properties);
		landCoverLayer.Save();
		return landCoverLayer;
	}

	public static LandCoverLayer CreateMerged(LandCoverLayer inputA, LandCoverLayer inputB, string filename, LandCoverType type, Dictionary<string, float> properties = null)
	{
		LandCoverLayer landCoverLayer = new LandCoverLayer(filename, null, new string[2] { inputA.SourceFilename, inputB.SourceFilename }, type, properties);
		landCoverLayer.Save();
		return landCoverLayer;
	}

	public static LandCoverLayer CreateCopy(LandCoverLayer copy, string filename, LandCoverType type, Dictionary<string, float> properties = null)
	{
		LandCoverLayer landCoverLayer = new LandCoverLayer(filename, null, new string[1] { copy.SourceFilename }, type, properties);
		landCoverLayer.Save();
		return landCoverLayer;
	}

	private void Initialize()
	{
		_overridePolygonLayer = new LandCoverClassificationLayer(this);
		Nodes.Add(_overridePolygonLayer);
		_overridePolygonLayer.FeatureChanged += [SpecialName] (object ob, FeatureChangedEventArgs ev) =>
		{
			DataChangedInternal();
		};
		_overridePolygonLayer.RegisterAttributeChangedAction("Name", [SpecialName] (DataRow dr) =>
		{
			DataChangedInternal();
		});
		_baseParameters = new ParameterSet();
	}

	private static string EnsureIsTiffFilename(string fn)
	{
		if (fn.RoughlyEndsWith(".tif"))
		{
			return fn;
		}
		return System.IO.Path.ChangeExtension(fn, "tif");
	}

	public static string EnsureIsHDF5Filename(string fn)
	{
		if (fn.RoughlyEndsWith(".hdf") || fn.RoughlyEndsWith(".h5"))
		{
			return fn;
		}
		return System.IO.Path.ChangeExtension(fn, "hdf");
	}

	private void DataChangedInternal()
	{
		_rasterExtentRead = false;
		_resampler = null;
		_classification = null;
		DataChanged?.Invoke();
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		if (!string.IsNullOrEmpty(SourceFilename))
		{
			SourceFilename = EnsureIsHDF5Filename(SourceFilename);
		}
		ReadLandCoverData();
	}

	protected override bool XMLLoadChild(XmlElement xmlEl)
	{
		if (Layer.GetTypeNameFromXML(xmlEl).Trim().RoughlyEquals("LandCoverClassificationLayer"))
		{
			_overridePolygonLayer.XMLLoad(xmlEl);
		}
		return true;
	}

	public override bool AllSourceFilesExist()
	{
		if (!File.Exists(HDFFilename))
		{
			return false;
		}
		if (VirtualDependencyMode)
		{
			LandCoverLayer[] virtualDependencies = EvaluateVirtualDependencies();
			if (virtualDependencies == null)
			{
				return false;
			}
			LandCoverLayer[] virtualDependenciesArray = virtualDependencies;
			for (int i = 0; i < virtualDependenciesArray.Length; i = checked(i + 1))
			{
				if (!virtualDependenciesArray[i].AllSourceFilesExist())
				{
					return false;
				}
			}
		}
		else if (HasRasterFile && !File.Exists(TiffFilename))
		{
			return false;
		}
		return true;
	}

	private void ReadLandCoverData()
	{
		try
		{
			if (!File.Exists(HDFFilename))
			{
				return;
			}
			using H5Reader hr = new H5Reader(HDFFilename);
			Version version = GetVersion(hr);
			if (version.Major == 1)
			{
				ReadLandCoverData_V1(hr);
				return;
			}
			if (version.Major == 2)
			{
				ReadLandCoverData_V2(hr);
				return;
			}
			throw new Exception("Version not recognized for " + HDFFilename);
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = ex2.Message;
			ProjectData.ClearProjectError();
		}
	}

	private void ReadLandCoverData_V1(H5Reader hr)
	{
		_baseRasterClassification = LandCoverReader.ParseLandCoverFile(hr.Filename, out _baseParameters, out _hasRasterFile);
		_hasRasterFile = true;
	}

	private void ReadLandCoverData_V2(H5Reader hr)
	{
		_baseParameters = new ParameterSet();
		hr.TryReadAttributeScalar("/", "GUID", ref _guid);
		checked
		{
			if (hr.ExistsAsDataset("/Raster Map"))
			{
				_hasRasterFile = true;
				Dictionary<CompoundFieldDefinition, Array> source = hr.ReadCompoundElements("/Raster Map");
				IEnumerable<KeyValuePair<CompoundFieldDefinition, Array>> source2 = source.Where([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "Name", TextCompare: false) == 0);
				if (source2.Count() != 1)
				{
					throw new Exception("Malformed Dataset");
				}
				KeyValuePair<CompoundFieldDefinition, Array> valuesForCompoundFieldDefinition = source2.First();
				IEnumerable<KeyValuePair<CompoundFieldDefinition, Array>> source3 = source.Where([SpecialName] (KeyValuePair<CompoundFieldDefinition, Array> kvp) => Operators.CompareString(kvp.Key.Name, "ID", TextCompare: false) == 0);
				if (source3.Count() != 1)
				{
					throw new Exception("Malformed Dataset");
				}
				KeyValuePair<CompoundFieldDefinition, Array> valuesForCompoundFieldDefinition2 = source3.First();
				_baseRasterClassification = new Classification();
				int count = valuesForCompoundFieldDefinition.Value.Length - 1;
				for (int ordinal = 0; ordinal <= count; ordinal++)
				{
					_baseRasterClassification.Set(Conversions.ToInteger(valuesForCompoundFieldDefinition2.Value.GetValue(ordinal)), Conversions.ToString(valuesForCompoundFieldDefinition.Value.GetValue(ordinal)));
				}
			}
			else
			{
				_hasRasterFile = false;
			}
			if (hr.ExistsAsDataset("/Variables"))
			{
				Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements("/Variables");
				string[] strings = null;
				foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
				{
					if (Operators.CompareString(compoundElement.Key.Name, "Name", TextCompare: false) == 0)
					{
						strings = compoundElement.Value as string[];
					}
				}
				if (strings == null)
				{
					throw new Exception("Malformed Variable dataset");
				}
				foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement2 in compoundElements)
				{
					if (Operators.CompareString(compoundElement2.Key.Name, "Name", TextCompare: false) != 0)
					{
						string name = compoundElement2.Key.Name;
						if (!(compoundElement2.Value is float[] floatValuesArray))
						{
							throw new Exception("Malformed Variable Dataset");
						}
						Parameter parameter = new Parameter(name);
						int count2 = floatValuesArray.Count() - 1;
						for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
						{
							parameter.AddOverride(strings[intervalIndex], floatValuesArray[intervalIndex]);
						}
						_baseParameters.Add(parameter);
					}
				}
			}
			if (hr.ExistsAsDataset("/Properties"))
			{
				Dictionary<CompoundFieldDefinition, Array> compoundElements2 = hr.ReadCompoundElements("/Properties");
				_properties = new Dictionary<string, float>();
				string[] stringsArray = new string[0];
				float[] floatValues = new float[0];
				foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement3 in compoundElements2)
				{
					if (Operators.CompareString(compoundElement3.Key.Name, "Name", TextCompare: false) == 0)
					{
						stringsArray = compoundElement3.Value as string[];
					}
					else if (Operators.CompareString(compoundElement3.Key.Name, "Value", TextCompare: false) == 0)
					{
						floatValues = compoundElement3.Value as float[];
					}
				}
				int count3 = stringsArray.Count() - 1;
				for (int intervalIndex2 = 0; intervalIndex2 <= count3; intervalIndex2++)
				{
					_properties.Add(stringsArray[intervalIndex2], floatValues[intervalIndex2]);
				}
			}
			if (hr.ExistsAsAttribute("/", "LC Type") && Enum.TryParse<LandCoverType>(hr.TryReadStringAttributeScalar("/", "LC Type"), out var result))
			{
				_landCoverType = result;
				if (_landCoverTypeToHelper.ContainsKey(_landCoverType))
				{
					LandCoverLayerHelper landCoverLayerHelper = _landCoverTypeToHelper[_landCoverType];
					using List<string>.Enumerator extraColumnDatasets = landCoverLayerHelper.ExtraColumnDatasets.GetEnumerator();
					_Closure_0024__104_002D0 capturedLocals = default;
					while (extraColumnDatasets.MoveNext())
					{
						capturedLocals = new _Closure_0024__104_002D0(capturedLocals);
						capturedLocals.parameter = extraColumnDatasets.Current;
						if (_baseParameters.Parameters.Find(capturedLocals._Lambda_0024__2) == null)
						{
							Parameter p = new Parameter(capturedLocals.parameter);
							_baseParameters.Add(p);
						}
					}
				}
			}
			if (!hr.ExistsAsAttribute("/", "Virtual Dependencies"))
			{
				return;
			}
			_isVirtualDependencyMode = true;
			hr.ReadAttribute("/", "Virtual Dependencies", ref _virtualDependencies);
			if (_virtualDependencies != null)
			{
				_virtualDependencies = _virtualDependencies.Select([SpecialName] (string fn) => RasMapperLib.Utilities.Path.MakeAbsolute(fn, SourceFilename)).ToArray();
			}
			if (hr.ExistsAsAttribute("/", "Virtual Dependency Types"))
			{
				string[] values = null;
				hr.ReadAttribute("/", "Virtual Dependency Types", ref values);
				_virtualDependencyTypes = new LandCoverType[values.Length - 1 + 1];
				int ordinal2 = 0;
				string[] strings2 = values;
				for (int intervalIndex3 = 0; intervalIndex3 < strings2.Length; intervalIndex3++)
				{
					Enum.TryParse<LandCoverType>(strings2[intervalIndex3], out var result2);
					_virtualDependencyTypes[ordinal2] = result2;
					ordinal2++;
				}
			}
		}
	}

	private static bool ValidateLayerIsVirtualDependency(H5Reader hr, LandCoverLayer potentialDependency, string requiredVDType)
	{
		checked
		{
			if (hr.ExistsAsDataset("/Dependency Classifications"))
			{
				if (potentialDependency.Classification == null)
				{
					return false;
				}
				Dictionary<CompoundFieldDefinition, Array> compoundElements = hr.ReadCompoundElements("/Dependency Classifications");
				string[] values = null;
				hr.ReadAttribute("/", "Virtual Dependency Types", ref values);
				List<int> intList = new List<int>();
				int ordinal = 0;
				string[] strings = values;
				for (int i = 0; i < strings.Length; i++)
				{
					if (Operators.CompareString(strings[i], requiredVDType, TextCompare: false) == 0)
					{
						intList.Add(ordinal);
					}
					ordinal++;
				}
				ordinal = 0;
				foreach (KeyValuePair<CompoundFieldDefinition, Array> compoundElement in compoundElements)
				{
					if (intList.Contains(ordinal))
					{
						Array compoundElementTerm = compoundElement.Value;
						bool condition = false;
						int count = compoundElementTerm.Length - 1;
						for (int j = 0; j <= count; j++)
						{
							if (Operators.CompareString(potentialDependency.Classification.Classes.ElementAtOrDefault(j), compoundElementTerm.GetValue(j).ToString(), TextCompare: false) != 0)
							{
								condition = true;
								break;
							}
						}
						if (!condition)
						{
							return true;
						}
					}
					ordinal++;
				}
			}
			return false;
		}
	}

	public void Save()
	{
		if (Classification == null)
		{
			return;
		}
		checked
		{
			using (H5Writer h5Writer = new H5Writer(SourceFilename))
			{
				h5Writer.WriteAttribute("/", "Version", CurrentVersion.ToString());
				if (_isVirtualDependencyMode)
				{
					WriteDependencyPathNames(h5Writer, _virtualDependencies);
					string[] values = _virtualLCDependencies.Select([SpecialName] (LandCoverLayer dep) => dep._landCoverType.ToString()).ToArray();
					h5Writer.WriteAttribute("/", "Virtual Dependency Types", values);
					int classesClassificationTerm = 0;
					int classeCount = 0;
					CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
					LandCoverLayer[] virtualLCDependencies = _virtualLCDependencies;
					foreach (LandCoverLayer landCoverLayer in virtualLCDependencies)
					{
						classesClassificationTerm = landCoverLayer.Classification.Classes.Max([SpecialName] (string textValue) => textValue.Length);
						compoundTypeDefinition.AddStringField(landCoverLayer.Name, classesClassificationTerm);
						if (landCoverLayer.Classification.Classes.Count > classeCount)
						{
							classeCount = landCoverLayer.Classification.Classes.Count;
						}
					}
					h5Writer.Create1DCompoundDataset("/Dependency Classifications", 100, compoundTypeDefinition);
					int count2 = classeCount - 1;
					for (int ordinal = 0; ordinal <= count2; ordinal++)
					{
						object[] objectValues = new object[_virtualLCDependencies.Count() - 1 + 1];
						int ordinal2 = 0;
						LandCoverLayer[] virtualLCDependencies2 = _virtualLCDependencies;
						foreach (LandCoverLayer landCoverLayer2 in virtualLCDependencies2)
						{
							if (ordinal < landCoverLayer2.Classification.Classes.Count)
							{
								objectValues[ordinal2] = landCoverLayer2.Classification.Classes.ElementAtOrDefault(ordinal);
							}
							else
							{
								objectValues[ordinal2] = "";
							}
							ordinal2++;
						}
						h5Writer.AddCompoundElement("/Dependency Classifications", objectValues);
					}
				}
				WritePropertiesIfExist(h5Writer);
				h5Writer.WriteAttribute("/", "LC Type", _landCoverType.ToString());
				if (string.IsNullOrEmpty(_guid))
				{
					_guid = Guid.NewGuid().ToString();
				}
				h5Writer.WriteAttribute("/", "GUID", _guid);
				if (h5Writer.ExistsAsDataset("ManningsN"))
				{
					h5Writer.Delete("ManningsN");
				}
				if (h5Writer.ExistsAsDataset("IDs"))
				{
					h5Writer.Delete("IDs");
				}
				if (h5Writer.ExistsAsDataset("Names"))
				{
					h5Writer.Delete("Names");
				}
				if (_hasRasterFile && _baseRasterClassification != null && _baseRasterClassification.Count != 0)
				{
					int maxStrLen = _baseRasterClassification.Classes.Max([SpecialName] (string textValue2) => textValue2.Length);
					CompoundTypeDefinition compoundTypeDefinition2 = new CompoundTypeDefinition();
					compoundTypeDefinition2.AddField<int>("ID");
					compoundTypeDefinition2.AddStringField("Name", maxStrLen);
					h5Writer.Create1DCompoundDataset("/Raster Map", 100, compoundTypeDefinition2);
					foreach (KeyValuePair<int, string> allClassification in _baseRasterClassification.GetAllClassifications())
					{
						h5Writer.AddCompoundElement("/Raster Map", new object[2] { allClassification.Key, allClassification.Value });
					}
				}
				ParameterSet parameters = Parameters;
				if (parameters.IsNullOrEmpty())
				{
					h5Writer.Delete("/Variables");
				}
				else
				{
					IClassification classification = Classification;
					if (classification.Count == 0)
					{
						throw new Exception("Bad Land Cover definition");
					}
					int maxStrLen2 = classification.Classes.Max([SpecialName] (string textValue3) => textValue3.Length);
					CompoundTypeDefinition compoundTypeDefinition3 = new CompoundTypeDefinition();
					compoundTypeDefinition3.AddStringField("Name", maxStrLen2);
					foreach (Parameter parameter in parameters)
					{
						compoundTypeDefinition3.AddField<float>(parameter.VariableName);
					}
					h5Writer.Create1DCompoundDataset("/Variables", 100, compoundTypeDefinition3);
					foreach (string @class in classification.Classes)
					{
						object[] objectValuesArray = new object[parameters.Count + 1];
						objectValuesArray[0] = @class;
						int count = parameters.Count;
						for (int unity = 1; unity <= count; unity++)
						{
							if (parameters[unity - 1].Values.TryGetValue(@class, out var localValue))
							{
								objectValuesArray[unity] = localValue;
							}
							else
							{
								objectValuesArray[unity] = -9999f;
							}
						}
						h5Writer.AddCompoundElement("/Variables", objectValuesArray);
					}
				}
			}
			_overridePolygonLayer.SaveFeatureTable();
		}
	}

	public void WritePropertiesIfExist(H5Writer hw, string groupPath = "")
	{
		if (_properties == null || _properties.Count <= 0)
		{
			return;
		}
		CompoundTypeDefinition compoundTypeDefinition = new CompoundTypeDefinition();
		int maxStrLen = _properties.Max([SpecialName] (KeyValuePair<string, float> kvp) => kvp.Key.Length);
		compoundTypeDefinition.AddStringField("Name", maxStrLen);
		compoundTypeDefinition.AddField<float>("Value");
		hw.Create1DCompoundDataset(groupPath + "/Properties", 100, compoundTypeDefinition);
		foreach (KeyValuePair<string, float> property in _properties)
		{
			hw.AddCompoundElement(row: new object[2] { property.Key, property.Value }, datasetName: groupPath + "/Properties");
		}
	}

	private static void WriteDependencyPathNames(H5Writer hw, string[] virtualDependencies)
	{
		string[] values = virtualDependencies.Select([SpecialName] (string fn) => RasMapperLib.Utilities.Path.MakeRelative(fn, hw.Filename)).ToArray();
		hw.WriteAttribute("/", "Virtual Dependencies", values);
	}

	public override List<string> GetAllSourceFiles()
	{
		List<string> allSourceFiles = new List<string>();
		allSourceFiles.Add(SourceFilename);
		if (!string.IsNullOrEmpty(TiffFilename))
		{
			allSourceFiles.Add(TiffFilename);
		}
		if (_isVirtualDependencyMode)
		{
			LandCoverLayer[] virtualLCDependencies = _virtualLCDependencies;
			foreach (LandCoverLayer landCoverLayer in virtualLCDependencies)
			{
				allSourceFiles.AddRange(landCoverLayer.GetAllSourceFiles());
			}
		}
		return allSourceFiles;
	}

	public override List<string> GetAllAvailableSourceFiles()
	{
		List<string> allSourceFiles = GetAllSourceFiles();
		if (allSourceFiles.Contains(TiffFilename) && !HasRasterFile)
		{
			allSourceFiles.Remove(TiffFilename);
		}
		return allSourceFiles;
	}

	public override List<string> GetAllVirtualizingFiles()
	{
		List<string> allVirtualizingFiles = base.GetAllVirtualizingFiles();
		LandCoverLayer[] virtualDependencies = EvaluateVirtualDependencies();
		if (virtualDependencies != null)
		{
			LandCoverLayer[] virtualDependenciesArray = virtualDependencies;
			foreach (LandCoverLayer landCoverLayer in virtualDependenciesArray)
			{
				ListExtensionMethods.RemoveRange(allVirtualizingFiles, landCoverLayer.GetAllVirtualizingFiles());
			}
		}
		return allVirtualizingFiles;
	}

	public override List<Layer> GetLayersThatDependOnMe()
	{
		List<Layer> layers = new List<Layer>();
		if (SharedData.RasMapper != null)
		{
			MapLayersGroup mapLayersGroup = SharedData.RasMapper.MapLayersGroup;
			foreach (Layer node in mapLayersGroup.Nodes)
			{
				if (node is LandCoverLayer && node != this && !layers.Contains(node))
				{
					LandCoverLayer[] virtualDependencies = ((LandCoverLayer)node).EvaluateVirtualDependencies();
					if (virtualDependencies != null && Enumerable.Contains(virtualDependencies, this))
					{
						layers.Add(node);
						layers.AddRange(node.GetLayersThatDependOnMe());
					}
				}
			}
		}
		return layers;
	}

	public static Version GetVersion(string filename)
	{
		using H5Reader hr = new H5Reader(filename);
		return GetVersion(hr);
	}

	public static Version GetVersion(H5Reader hr)
	{
		string textValue = "";
		if (hr.TryReadAttributeScalar("/", "Version", ref textValue))
		{
			return new Version(textValue);
		}
		return new Version(1, 0);
	}

	private List<Tuple<Geospatial.Vectors.Polygon, int>> GetPolysAsMappedTuples(IClassification cls)
	{
		List<Tuple<Geospatial.Vectors.Polygon, int>> polysAsMappedTuples = new List<Tuple<Geospatial.Vectors.Polygon, int>>();
		checked
		{
			for (int i = _overridePolygonLayer.FeatureCount() - 1; i >= 0; i += -1)
			{
				string classificationName = _overridePolygonLayer.GetClassificationName(i);
				if (IsValidClassificationName(classificationName))
				{
					int iD = cls.GetID(classificationName);
					if (iD > 0)
					{
						polysAsMappedTuples.Add(new Tuple<Geospatial.Vectors.Polygon, int>(Converter.Convert(_overridePolygonLayer.Polygon(i)), iD));
					}
				}
			}
			return polysAsMappedTuples;
		}
	}

	private IClassification GetVirtualClassification()
	{
		LandCoverLayer[] virtualDependencies = EvaluateVirtualDependencies();
		if (virtualDependencies == null)
		{
			return null;
		}
		if (virtualDependencies.Count() > 2 || virtualDependencies.Count() == 0)
		{
			return null;
		}
		LandCoverLayer landCoverLayer = virtualDependencies[0];
		if (!landCoverLayer.AllSourceFilesExist())
		{
			return null;
		}
		LandCoverLayer landCoverLayer2 = null;
		if (virtualDependencies.Length > 1)
		{
			landCoverLayer2 = virtualDependencies[1];
			if (!landCoverLayer2.AllSourceFilesExist())
			{
				return null;
			}
		}
		if (landCoverLayer2 != null)
		{
			return new MergedClassification(landCoverLayer.Classification, landCoverLayer2.Classification);
		}
		return landCoverLayer.Classification;
	}

	private LandCoverLayer[] EvaluateVirtualDependencies()
	{
		if (_virtualLCDependencies != null)
		{
			return _virtualLCDependencies;
		}
		if (!_isVirtualDependencyMode)
		{
			return null;
		}
		checked
		{
			_virtualLCDependencies = new LandCoverLayer[_virtualDependencies.Count() - 1 + 1];
			int count = _virtualDependencies.Count() - 1;
			_Closure_0024__122_002D0 capturedLocals = default;
			for (int i = 0; i <= count; i++)
			{
				capturedLocals = new _Closure_0024__122_002D0(capturedLocals);
				capturedLocals.fn = _virtualDependencies[i];
				if (File.Exists(capturedLocals.fn) || _virtualDependencyTypes == null)
				{
					_virtualLCDependencies[i] = Layer.TryFindOrCreateLayer(capturedLocals.fn, SharedData.RasMapper?.MapLayersGroup, capturedLocals._Lambda_0024__0);
				}
				_virtualLCDependencies[i].DataChanged += BaseDataUpdated;
			}
			return _virtualLCDependencies;
		}
	}

	private void BaseDataUpdated()
	{
		_classification = null;
		_resampler = null;
		DataChanged?.Invoke();
	}

	public float[] GetTileValues(float[] paramValues, RasterM tile, string paramName, ParameterSet overrideParameters = null)
	{
		_Closure_0024__124_002D0 capturedLocals = default;
		_Closure_0024__124_002D0 capturedLocals2 = new _Closure_0024__124_002D0(capturedLocals);
		capturedLocals2.paramName = paramName;
		if (overrideParameters == null)
		{
			overrideParameters = Parameters;
		}
		int[] values = null;
		Rasterize(tile, ref values);
		checked
		{
			if (Operators.CompareString(capturedLocals2.paramName, "ID", TextCompare: false) == 0)
			{
				int count = values.Length - 1;
				for (int i = 0; i <= count; i++)
				{
					paramValues[i] = values[i];
				}
			}
			else
			{
				int count2 = values.Length - 1;
				for (int j = 0; j <= count2; j++)
				{
					string name = Classification.GetName(values[j]);
					Parameter parameter = overrideParameters.Parameters.Where([SpecialName] (Parameter paramInParameters) => Operators.CompareString(paramInParameters.VariableName, capturedLocals2.paramName, TextCompare: false) == 0).First();
					if (parameter.Values.ContainsKey(name))
					{
						paramValues[j] = parameter.Values[name];
					}
				}
			}
			return paramValues;
		}
	}

	public bool TryAssigningNewParamtersUsingTable(DataTable tbl, bool save)
	{
		Classification classification = null;
		ParameterSet vars = null;
		List<int> IDsToExclude = null;
		if (ParseClassificationTables(tbl, ref classification, ref vars, ref IDsToExclude))
		{
			string changeExtension = System.IO.Path.ChangeExtension(HDFFilename, ".backup.hdf");
			FileHelpers.TryDelete(changeExtension);
			File.Copy(HDFFilename, changeExtension);
			_baseParameters = vars;
			if (save)
			{
				Save();
				ReadLandCoverData();
			}
			DataChangedInternal();
			return true;
		}
		MessageBox.Show("Error Parsing Table");
		return false;
	}

	public static bool TryLoadLayer(string hdfFilename, ref LandCoverLayer lyr, ref string errMessage, LandCoverType type = LandCoverType.LandCover)
	{
		_Closure_0024__126_002D0 capturedLocals = new _Closure_0024__126_002D0(null);
		capturedLocals.hdfFilename = hdfFilename;
		checked
		{
			bool result;
			if (!File.Exists(capturedLocals.hdfFilename))
			{
				errMessage = capturedLocals.hdfFilename.InSingleQuotes() + " does not exist.";
				result = false;
			}
			else
			{
				string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(capturedLocals.hdfFilename);
				try
				{
					Action action = null;
					using (H5Reader h5Reader = new H5Reader(capturedLocals.hdfFilename))
					{
						if (!h5Reader.ExistsAsDataset("/Raster Map"))
						{
							goto IL_009c;
						}
						string changeExtension = System.IO.Path.ChangeExtension(capturedLocals.hdfFilename, ".tif");
						if (File.Exists(changeExtension))
						{
							goto IL_009c;
						}
						errMessage = "Required data file " + changeExtension.InSingleQuotes() + " does not exist.";
						result = false;
						goto end_IL_0054;
						IL_009c:
						if (h5Reader.ExistsAsAttribute("/", "Virtual Dependencies"))
						{
							_Closure_0024__126_002D1 capturedLocals2 = default;
							_Closure_0024__126_002D1 capturedLocals3 = new _Closure_0024__126_002D1(capturedLocals2);
							capturedLocals3.outerClosure = capturedLocals;
							string[] values = null;
							h5Reader.ReadAttribute("/", "Virtual Dependencies", ref values);
							string[] values2 = null;
							if (h5Reader.ExistsAsAttribute("/", "Virtual Dependency Types"))
							{
								h5Reader.ReadAttribute("/", "Virtual Dependency Types", ref values2);
							}
							values = values.Select([SpecialName] (string fn) => RasMapperLib.Utilities.Path.MakeAbsolute(fn, capturedLocals3.outerClosure.hdfFilename)).ToArray();
							capturedLocals3.newVirtualDependencyPathNames = (string[])values.Clone();
							int ordinal = 0;
							string[] strings = values;
							int ordinal2 = 0;
							while (true)
							{
								if (ordinal2 >= strings.Length)
								{
									if (Enumerable.SequenceEqual(capturedLocals3.newVirtualDependencyPathNames, values))
									{
										break;
									}
									action = [SpecialName] () =>
									{
										using H5Writer hw = new H5Writer(capturedLocals3.outerClosure.hdfFilename);
										WriteDependencyPathNames(hw, capturedLocals3.newVirtualDependencyPathNames);
									};
									break;
								}
								string textValue = strings[ordinal2];
								if (!File.Exists(textValue) && values2 != null)
								{
									result = false;
								}
								else
								{
									if (File.Exists(textValue) || values2 != null)
									{
										ordinal++;
										ordinal2++;
										continue;
									}
									errMessage = "Required data file " + textValue.InSingleQuotes() + " does not exist.\r\nYou should probably remake this layer, it's out of date (this message should not appear in production)";
									result = false;
								}
								goto end_IL_0054;
							}
						}
						if (GetVersion(h5Reader).Major != 1 || IsCorrectlyFormatted(h5Reader, ref errMessage))
						{
							goto IL_01e5;
						}
						result = false;
						end_IL_0054:;
					}
					goto end_IL_0045;
					IL_01e5:
					action?.Invoke();
					lyr = new LandCoverLayer(fileNameWithoutExtension, capturedLocals.hdfFilename, type);
					result = true;
					end_IL_0045:;
				}
				catch (Exception ex)
				{
					ProjectData.SetProjectError(ex);
					Exception ex2 = ex;
					errMessage = ex2.Message;
					result = false;
					ProjectData.ClearProjectError();
				}
			}
			return result;
		}
	}

	public void Export(string tiffExportFilename, double cellSize, Func<float[], RasterM, float[]> getTileValues)
	{
		Extent extent = Extent;
		checked
		{
			int systemWidthTerm = (int)System.Math.Round(extent.Width / cellSize);
			int systemHeightTerm = (int)System.Math.Round(extent.Height / cellSize);
			TiffMetadata<float> metadata = new TiffMetadata<float>(-9999f, RoundTo.None);
			using (FloatTiffWriter floatTiffWriter = new FloatTiffWriter(tiffExportFilename, systemWidthTerm, systemHeightTerm, ComputeStats: true, metadata))
			{
				int numTilesWide = floatTiffWriter.GetNumTilesWide();
				int count = floatTiffWriter.GetNumTilesTall() - 1;
				for (int i = 0; i <= count; i++)
				{
					int count2 = numTilesWide - 1;
					for (int j = 0; j <= count2; j++)
					{
						int tileNumber = i * numTilesWide + j;
						int ordinal = j * 256;
						int ordinal2 = i * 256;
						double leftExtentTerm = extent.Left + (double)ordinal * cellSize;
						double topExtentTerm = extent.Top - (double)ordinal2 * cellSize;
						double maxX = leftExtentTerm + 256.0 * cellSize;
						double minY = topExtentTerm - 256.0 * cellSize;
						RasterM rasterM = new RasterM(256, 256, maxX, leftExtentTerm, topExtentTerm, minY);
						float[] floatValues = new float[65536];
						Buffers.ArraySetAllVB(floatValues, -9999f);
						floatValues = getTileValues(floatValues, rasterM);
						floatTiffWriter.WriteTile(tileNumber, floatValues);
					}
				}
			}
			InterpolatedLayer.AddSRSGeorefAddo(tiffExportFilename, systemWidthTerm, systemHeightTerm, extent.Left, extent.Top, cellSize, UseNearest: false, null, null, null);
		}
	}

	private static bool IsCorrectlyFormatted(H5Reader hr, ref string errMessage)
	{
		if (!hr.ExistsAsDataset("Names"))
		{
			errMessage = "Dataset Names does not exist.";
			return false;
		}
		if (!hr.ExistsAsDataset("IDs"))
		{
			errMessage = "Dataset IDs does not exist.";
			return false;
		}
		return true;
	}

	public byte[] ComputeDataHash()
	{
		if (!File.Exists(HDFFilename))
		{
			return null;
		}
		Hash hash = new Hash();
		if (HasRasterFile)
		{
			if (!File.Exists(TiffFilename))
			{
				return null;
			}
			hash.Add(new DateTime[1] { File.GetLastWriteTime(TiffFilename) });
		}
		hash.Add(new DateTime[1] { File.GetLastWriteTime(HDFFilename) });
		_overridePolygonLayer.HashAppend(hash);
		return hash.HashBytes();
	}

	public override DateTime DateLastModified()
	{
		if (!File.Exists(HDFFilename))
		{
			return DateTime.MinValue;
		}
		DateTime dateTime = File.GetLastWriteTime(HDFFilename);
		if (VirtualDependencyMode)
		{
			LandCoverLayer[] virtualDependencies = EvaluateVirtualDependencies();
			if (virtualDependencies == null)
			{
				return dateTime;
			}
			LandCoverLayer[] virtualDependenciesArray = virtualDependencies;
			for (int i = 0; i < virtualDependenciesArray.Length; i = checked(i + 1))
			{
				DateTime dateTime2 = virtualDependenciesArray[i].DateLastModified();
				if (DateTime.Compare(dateTime2, dateTime) > 0)
				{
					dateTime = dateTime2;
				}
			}
			return dateTime;
		}
		DateTime dateTime3 = DateTime.MinValue;
		if (HasRasterFile && File.Exists(TiffFilename))
		{
			dateTime3 = File.GetLastWriteTime(TiffFilename);
		}
		if (DateTime.Compare(dateTime, dateTime3) > 0)
		{
			return dateTime;
		}
		return dateTime3;
	}

	public bool Rasterize(RasterM raster, ref int[] values)
	{
		bool result;
		try
		{
			if (!File.Exists(SourceFilename))
			{
				ErrorMessage = "Unable to find file: " + SourceFilename.InSingleQuotes();
				result = false;
			}
			else
			{
				IClassificationResampler resampler = Resampler;
				if (resampler == null)
				{
					ErrorMessage = "Could not initialize reader for " + Name + ": " + SourceFilename.InSingleQuotes();
					result = false;
				}
				else
				{
					RasterDefinition queryRaster = Converter.Convert(raster);
					resampler.Resample(new ResampleArgs(queryRaster), values);
					result = true;
				}
			}
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			ErrorMessage = "Error getting data from file: " + SourceFilename.InSingleQuotes() + ex2.Message;
			result = false;
			ProjectData.ClearProjectError();
		}
		return result;
	}

	public static bool IsValidClassificationName(string className)
	{
		if (string.IsNullOrEmpty(className))
		{
			return false;
		}
		if (className.Contains("/") || className.Contains("\\"))
		{
			return false;
		}
		if (className.RoughlyEquals("NoData"))
		{
			return false;
		}
		return true;
	}

	private HashSet<string> GetValidClassificationNames()
	{
		HashSet<string> validClassificationNames = new HashSet<string>();
		checked
		{
			int count = _overridePolygonLayer.FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string classificationName = _overridePolygonLayer.GetClassificationName(i);
				if (IsValidClassificationName(classificationName))
				{
					validClassificationNames.Add(classificationName);
				}
			}
			return validClassificationNames;
		}
	}

	public static DataTable GetClassificationVariablesAsDataTable(IClassification classification, ParameterSet parameters)
	{
		if (parameters == null)
		{
			parameters = new ParameterSet();
		}
		DataTable dataTable = new DataTable();
		DataColumn dataColumn = new DataColumn("Name", typeof(string));
		DataColumn dataColumn2 = new DataColumn("ID", typeof(int));
		dataColumn.ReadOnly = true;
		dataColumn2.ReadOnly = true;
		dataTable.Columns.Add(dataColumn2);
		dataTable.Columns.Add(dataColumn);
		checked
		{
			int count = parameters.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Parameter parameter = parameters[i];
				dataTable.Columns.Add(new DataColumn(parameter.VariableName, typeof(float)));
			}
			if (classification != null)
			{
				foreach (KeyValuePair<int, string> allClassification in classification.GetAllClassifications())
				{
					DataRow dataRow = dataTable.NewRow();
					dataRow[dataColumn] = allClassification.Value;
					dataRow[dataColumn2] = allClassification.Key;
					AssignColumnsInRow(dataRow, allClassification.Value, parameters);
					dataTable.Rows.Add(dataRow);
				}
			}
			return dataTable;
		}
	}

	private static void AssignColumnsInRow(DataRow row, string className, ParameterSet parameters)
	{
		checked
		{
			int count = parameters.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Parameter parameter = parameters[i];
				AssignColumnInRow(row, className, parameter, parameter.VariableName);
			}
		}
	}

	private static void AssignColumnInRow(DataRow row, string className, Parameter parameter, string colName)
	{
		if (parameter.Values.ContainsKey(className))
		{
			float row2 = parameter.Values[className];
			if (row2 == -9999f || row2 == float.MaxValue || row2 == float.MinValue || float.IsNaN(row2))
			{
				row[colName] = DBNull.Value;
			}
			else
			{
				row[colName] = row2;
			}
		}
		else
		{
			row[colName] = DBNull.Value;
		}
	}

	public static ParameterSet CopyVariableNamesFromParameterSet(ParameterSet copyFrom)
	{
		return new ParameterSet(copyFrom.Select([SpecialName] (Parameter para) => new Parameter(para.VariableName)).ToList());
	}

	public static DataTable GetClassificationVariablesAsDataTable(IClassification classification, ParameterSet parameterDefinitions, ParameterSet parameterValues)
	{
		DataTable classificationVariablesAsDataTable = GetClassificationVariablesAsDataTable(classification, parameterDefinitions);
		ParameterSet parameterSet = CopyVariableNamesFromParameterSet(parameterDefinitions);
		using IEnumerator<Parameter> parameterSet2 = parameterSet.GetEnumerator();
		_Closure_0024__138_002D0 capturedLocals = default;
		while (parameterSet2.MoveNext())
		{
			capturedLocals = new _Closure_0024__138_002D0(capturedLocals);
			capturedLocals.parameter = parameterSet2.Current;
			DataColumn dataColumn = new DataColumn("Base Override - " + capturedLocals.parameter.VariableName);
			classificationVariablesAsDataTable.Columns.Add(dataColumn);
			Parameter parameter = parameterValues.Parameters.Find(capturedLocals._Lambda_0024__0);
			if (parameter == null)
			{
				continue;
			}
			foreach (DataRow row in classificationVariablesAsDataTable.Rows)
			{
				string className = row["Name"].ToString();
				AssignColumnInRow(row, className, parameter, dataColumn.ColumnName);
			}
		}
		return classificationVariablesAsDataTable;
	}

	public static bool ParseClassificationTablesForBaseOverrides(DataTable dt, ParameterSet parameterDefinitions, ref ParameterSet baseOverrideParams, string featureName = null)
	{
		checked
		{
			bool result2;
			try
			{
				featureName = ((featureName != null) ? (featureName + " - ") : "Base Override - ");
				baseOverrideParams = new ParameterSet();
				int count = dt.Columns.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					string columnName = dt.Columns[i].ColumnName;
					if (!columnName.RoughlyEquals("Name") && !columnName.RoughlyEquals("ID") && columnName.Contains(featureName))
					{
						columnName = columnName.Substring(featureName.Length);
						baseOverrideParams.Add(new Parameter(columnName));
					}
				}
				_Closure_0024__139_002D0 capturedLocals = default;
				foreach (DataRow row in dt.Rows)
				{
					string key = row["Name"] as string;
					Conversions.ToInteger(row["ID"]);
					int count2 = dt.Columns.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						capturedLocals = new _Closure_0024__139_002D0(capturedLocals);
						capturedLocals.cName = dt.Columns[j].ColumnName;
						if (!capturedLocals.cName.RoughlyEquals("Name") && !capturedLocals.cName.RoughlyEquals("ID") && capturedLocals.cName.Contains(featureName))
						{
							capturedLocals.cName = capturedLocals.cName.Substring(featureName.Length);
							object objectValue = RuntimeHelpers.GetObjectValue(row[j]);
							if (!Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)) && !string.IsNullOrEmpty(Conversions.ToString(objectValue)) && float.TryParse(Conversions.ToString(objectValue), out var result) && !float.IsNaN(result))
							{
								baseOverrideParams.First(capturedLocals._Lambda_0024__0).Values[key] = result;
							}
						}
					}
				}
				result2 = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result2 = false;
				ProjectData.ClearProjectError();
			}
			return result2;
		}
	}

	public static bool ParseClassificationTables(DataTable dt, ref Classification classification, ref ParameterSet vars, ref List<int> IDsToExclude)
	{
		checked
		{
			bool result;
			try
			{
				classification = new Classification();
				vars = new ParameterSet();
				IDsToExclude = new List<int>();
				int count = dt.Columns.Count - 1;
				for (int i = 0; i <= count; i++)
				{
					string columnName = dt.Columns[i].ColumnName;
					if (!columnName.RoughlyEquals("Name") && !columnName.RoughlyEquals("ID"))
					{
						vars.Add(new Parameter(columnName));
					}
				}
				_Closure_0024__140_002D0 capturedLocals = default;
				foreach (DataRow row in dt.Rows)
				{
					string row2 = row["Name"] as string;
					int id = Conversions.ToInteger(row["ID"]);
					classification.Set(id, row2);
					int count2 = dt.Columns.Count - 1;
					for (int j = 0; j <= count2; j++)
					{
						capturedLocals = new _Closure_0024__140_002D0(capturedLocals);
						capturedLocals.cName = dt.Columns[j].ColumnName;
						if (!capturedLocals.cName.RoughlyEquals("Name") && !capturedLocals.cName.RoughlyEquals("ID"))
						{
							object objectValue = RuntimeHelpers.GetObjectValue(row[j]);
							if (!Information.IsDBNull(RuntimeHelpers.GetObjectValue(objectValue)))
							{
								float conversionsObjectValueTerm = Conversions.ToSingle(objectValue);
								vars.First(capturedLocals._Lambda_0024__0).Values[row2] = conversionsObjectValueTerm;
							}
						}
					}
				}
				result = true;
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				result = false;
				ProjectData.ClearProjectError();
			}
			return result;
		}
	}

	public float[] TryGetOverrideValues(string variableName)
	{
		_Closure_0024__141_002D0 capturedLocals = default;
		_Closure_0024__141_002D0 capturedLocals2 = new _Closure_0024__141_002D0(capturedLocals);
		capturedLocals2.variableName = variableName;
		checked
		{
			float[] overrideValues = new float[Count - 1 + 1];
			int count = Count - 1;
			for (int i = 0; i <= count; i++)
			{
				overrideValues[i] = -9999f;
			}
			Parameter parameter = _baseParameters?.FirstOrDefault([SpecialName] (Parameter cv) => cv.VariableName.RoughlyEquals(capturedLocals2.variableName));
			if (parameter == null)
			{
				return overrideValues;
			}
			string[] names = GetNames();
			int count2 = Count - 1;
			for (int intervalIndex = 0; intervalIndex <= count2; intervalIndex++)
			{
				string key = names[intervalIndex];
				if (parameter.Values.TryGetValue(key, out var overrideValues2))
				{
					overrideValues[intervalIndex] = overrideValues2;
				}
				else
				{
					overrideValues[intervalIndex] = -9999f;
				}
			}
			return overrideValues;
		}
	}

	public float[] TryGetManningsOverrideValues()
	{
		return TryGetOverrideValues("ManningsN");
	}

	private string TryGetLCName(int id)
	{
		return Classification.GetName(id);
	}

	public Parameter TryGetManningsN()
	{
		return _baseParameters?.FirstOrDefault([SpecialName] (Parameter cv) => cv.VariableName.RoughlyEquals("ManningsN"));
	}

	private string TryGetShortVariableName(string varname)
	{
		if (varname.RoughlyEquals("ManningsN"))
		{
			return "n";
		}
		if (varname.RoughlyEquals("Percent Impervious"))
		{
			return "%I";
		}
		return varname;
	}

	public bool TryGetPropertyValue(string name, ref float floatValue)
	{
		if (_properties.ContainsKey(name))
		{
			floatValue = _properties[name];
			return true;
		}
		floatValue = float.NaN;
		return false;
	}

	public bool TrySetPropertyValue(string name, float properties)
	{
		if (_properties.ContainsKey(name))
		{
			_properties[name] = properties;
			return true;
		}
		return false;
	}

	public string[] GetNames()
	{
		return Classification.GetAllClassifications().Values.ToArray();
	}

	public int[] GetIDs()
	{
		return Classification.GetAllClassifications().Keys.ToArray();
	}
}
