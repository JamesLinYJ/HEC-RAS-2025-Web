using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Geospatial;
using Geospatial.Rasters;
using Geospatial.Rasters.Classifications;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.Extensions;
using Utility.Hashing;
using Utility.Logging;
using Utility.Memory;
using Utility.ORM;

namespace Ras.Layers;

public class NValueLayer : CoreLayerBase, IResample<float>, ISurface<float>, ISpatialVersion
{
	public static class MessageCodes
	{
		public static readonly MessageClass NoClassifierAssociated = new MessageClass("NVLayer_NoAssociatedClassifier", MessageSeverity.Error, "\"{LayerName}\" has no associated Classification Layer.");

		public static readonly MessageClass AssociatedClassNotFound = new MessageClass("NVLayer_AssociatedClassifierNotFound", MessageSeverity.Error, "\"{LayerName}\" has a missing/invalid Classification association: \"{ExpectedClassifier}\".");

		public static readonly MessageClass NVTableLoadError = new MessageClass("NVLayer_NVTableNotFound", MessageSeverity.Error, "{LayerName} is missing required table mapping classifier names to n-values.");

		public static Message GetNoClassifierAssociated(string layerName)
		{
			return NoClassifierAssociated.Create(layerName);
		}

		public static Message GetAssociatedClassNotFound(string layerName, string classifierName)
		{
			return AssociatedClassNotFound.Create(layerName, classifierName);
		}

		public static Message GetNVTableLoadError(string layerName)
		{
			return NVTableLoadError.Create(layerName);
		}
	}

	private new static readonly ILogger _logger = LogHelper.GetLogger("NValueLayer");

	public const string HDF5Group = "/NValues";

	public static readonly string FileVersion = Project.DevFileVersion;

	public const string AttributeDatasetName = "Attributes";

	public const string Extension = ".h5";

	public const float DefaultConstantValue = 0.06f;

	public const string TableColumnClassName = "Class";

	public const string TableColumnNVName = "N Value";

	public static readonly ConstantValueResampler<float> NoDataResampler = new ConstantValueResampler<float>(-9999f, Extent.EffectivelyInfinite, -9999f);

	private NValueSource _mode;

	private float _constantValue;

	private VersionTracker _classificationVerTracker;

	private readonly object _loadLock = new object();

	private ClassificationLayer _associatedClassificationLayer;

	private VersionTracker _interpTableTracker;

	private NVTable _interpretationTable = new NVTable();

	private long _versionXy;

	private Freezer _freezer;

	private readonly Versioned<IResample<float>> _resampler = new Versioned<IResample<float>>();

	public Extent ValidExtent => Resampler?.ValidExtent ?? Extent.NullExtent;

	public float? NoDataValue => Resampler?.NoDataValue;

	[HashContributor]
	[Metadata("Mode")]
	public NValueSource Mode
	{
		get
		{
			return _mode;
		}
		set
		{
			if (_mode != value)
			{
				_mode = value;
				_versionXy++;
			}
		}
	}

	[HashContributor]
	[Metadata("Constant Value")]
	public float ConstantValue
	{
		get
		{
			return _constantValue;
		}
		set
		{
			if (_constantValue != value)
			{
				_constantValue = value;
				_versionXy++;
			}
		}
	}

	public ClassificationLayer AssociatedClassificationLayer
	{
		get
		{
			if (_associatedClassificationLayer != null)
			{
				return _associatedClassificationLayer;
			}
			if (string.IsNullOrEmpty(ClassificationLayerName))
			{
				return null;
			}
			return LazyThreadsafe<ClassificationLayer>.AllocateOnce(ref _associatedClassificationLayer, _loadLock, this, (NValueLayer model) =>
			{
				Project project = model.Project;
				if (project != null)
				{
					return project.ClassificationLayers.FirstOrDefault((ClassificationLayer cls) => cls.Name == model.ClassificationLayerName);
				}
				string path = Path.Combine(Path.GetDirectoryName(model.Filename), model.ClassificationLayerName + ".h5");
				return File.Exists(path) ? new ClassificationLayer(path) : null;
			});
		}
		set
		{
			_classificationVerTracker.Assign(ref _associatedClassificationLayer, value);
			ClassificationLayerName = _associatedClassificationLayer?.Name ?? "";
		}
	}

	[HashContributor]
	[Metadata("Classification Layer Name")]
	public string ClassificationLayerName { get; set; }

	[HashContributor]
	public NVTable InterpretationTable
	{
		get
		{
			return _interpretationTable;
		}
		set
		{
			_interpTableTracker.Assign(ref _interpretationTable, value);
		}
	}

	public long VersionXY
	{
		get
		{
			if (_freezer.IsFrozen)
			{
				return _freezer.FrozenVersionXY;
			}
			return _versionXy + _classificationVerTracker.GetVersion(AssociatedClassificationLayer) + _interpTableTracker.GetVersion(_interpretationTable);
		}
	}

	public IResample<float> Resampler => _resampler.Get(VersionXY, BuildResampler);

	public static string MakeFilename(string projectDir, string nvalName)
	{
		return Project.MakeFilename<NValueLayer>(projectDir, nvalName, ".h5");
	}

	protected override string GetHDF5Group()
	{
		return "/NValues";
	}

	public override string GetCurrentFileFormatVersion()
	{
		return FileVersion;
	}

	public void FreezeOne()
	{
		_freezer.FreezeOne(this);
	}

	public void ThawOne()
	{
		_freezer.ThawOne();
	}

	private NValueLayer(InstantiationMode mode)
		: base(mode)
	{
	}

	public NValueLayer(string filename, Project project = null)
		: base(filename, project)
	{
	}

	public static NValueLayer CreateInMemory(string name = "")
	{
		return new NValueLayer(InstantiationMode.InMemory)
		{
			_name = name
		};
	}

	public static NValueLayer CreateInMemory(float nv)
	{
		NValueLayer nValueLayer = CreateInMemory();
		nValueLayer.SetConstant(nv);
		nValueLayer.Name = "NValue Layer " + nv;
		return nValueLayer;
	}

	public static NValueLayer CreateNew(string filename)
	{
		CoreLayerBase.VerifyCanCreateNew(filename);
		NValueLayer nValueLayer = new NValueLayer(InstantiationMode.CreateNewFile);
		nValueLayer.SetConstant(0.06f);
		nValueLayer.InitializeAndSaveAsNew(filename);
		return nValueLayer;
	}

	public static NValueLayer Load(NativeResourceWrapper<H5Reader> reader, string name = "")
	{
		NValueLayer nValueLayer = new NValueLayer(InstantiationMode.LoadFromDataProvider);
		nValueLayer._h5Wrapper = reader;
		nValueLayer.InitializeData();
		if (!string.IsNullOrEmpty(name))
		{
			nValueLayer._name = name;
		}
		return nValueLayer;
	}

	public static bool IsValid(string fn)
	{
		try
		{
			if (!File.Exists(fn))
			{
				return false;
			}
			string extension = Path.GetExtension(fn);
			if (extension.RoughlyEquals(".h5") || extension.RoughlyEquals(".hdf"))
			{
				using (H5Reader hr = new H5Reader(fn))
				{
					return IsValid(hr);
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public static bool IsValid(H5Reader hr)
	{
		return hr.ExistsAsGroup("/NValues");
	}

	protected override bool NeedsSaveInternal()
	{
		return false;
	}

	protected override void SaveInternal(H5Writer hw)
	{
		string pathCombine = H5Reader.PathCombine("/NValues", "Table");
		if (InterpretationTable == null || InterpretationTable.Count == 0)
		{
			hw.Delete(pathCombine);
			return;
		}
		Table table = new Table();
		table.AddColumn("Class", typeof(string));
		table.AddColumn("N Value", typeof(float));
		foreach (KeyValuePair<string, float> rawDatum in InterpretationTable.RawData)
		{
			table.AddNewRow(new object[2] { rawDatum.Key, rawDatum.Value });
		}
		hw.WriteCompoundTable(pathCombine, table, new CompoundTableOptions
		{
			CompressDataset = false
		});
	}

	protected override void LoadInternal(H5Reader hr)
	{
		base.LoadInternal(hr);
		string pathCombine = H5Reader.PathCombine("/NValues", "Table");
		if (!hr.ExistsAsDataset(pathCombine))
		{
			return;
		}
		Table table = hr.ReadCompoundElementsAsTable(pathCombine);
		if (table.GetColumnIndex("Class") == -1)
		{
			AddLoadError(H5Reader.MessageCodes.GetCompoundTableMissingRequiredColumn(hr.Filename, pathCombine, "Class", typeof(string)));
			return;
		}
		if (table.GetColumn("Class").Type != typeof(string))
		{
			AddLoadError(H5Reader.MessageCodes.GetCompoundTableMissingRequiredColumn(hr.Filename, pathCombine, "Class", typeof(string)));
			return;
		}
		int columnIndex = table.GetColumnIndex("N Value");
		if (columnIndex == -1)
		{
			AddLoadError(H5Reader.MessageCodes.GetCompoundTableMissingRequiredColumn(hr.Filename, pathCombine, "N Value", typeof(float)));
			return;
		}
		if (table.Columns[columnIndex].Type != typeof(float))
		{
			AddLoadError(H5Reader.MessageCodes.GetCompoundTableMissingRequiredColumn(hr.Filename, pathCombine, "N Value", typeof(float)));
			return;
		}
		NVTable nVTable = new NVTable();
		for (int i = 0; i < table.Rows.Count; i++)
		{
			string key = (string)table.Rows[i][0];
			float row = (float)table.Rows[i][1];
			nVTable.Set(key, row);
		}
		InterpretationTable = nVTable;
	}

	public void SetConstant(float constant)
	{
		Mode = NValueSource.Constant;
		ConstantValue = constant;
	}

	public void SetTrackClassificationLayer(ClassificationLayer layer)
	{
		Mode = NValueSource.ClassificationLayer;
		AssociatedClassificationLayer = layer;
	}

	private Dictionary<int, float> TryBuildClassFloatMap()
	{
		ClearLoadErrorsOf(MessageCodes.AssociatedClassNotFound, MessageCodes.NVTableLoadError);
		ClassificationLayer associatedClassificationLayer = AssociatedClassificationLayer;
		if (associatedClassificationLayer == null)
		{
			if (string.IsNullOrEmpty(ClassificationLayerName))
			{
				AddLoadError(MessageCodes.GetNoClassifierAssociated(Name));
			}
			else
			{
				AddLoadError(MessageCodes.GetAssociatedClassNotFound(Name, ClassificationLayerName));
			}
			return null;
		}
		if (InterpretationTable == null || InterpretationTable.RawData == null)
		{
			AddLoadError(MessageCodes.GetNVTableLoadError(Name));
			return null;
		}
		UniqueClassificationMap classificationMap = associatedClassificationLayer.ClassificationMap;
		Dictionary<int, float> floatByInt = new Dictionary<int, float>();
		foreach (KeyValuePair<string, float> rawDatum in InterpretationTable.RawData)
		{
			if (classificationMap.TryGetID(rawDatum.Key, out var localValue))
			{
				floatByInt[localValue] = rawDatum.Value;
			}
		}
		return floatByInt;
	}

	private IResample<float> BuildResampler()
	{
		ClearLoadErrorsOf(MessageCodes.AssociatedClassNotFound);
		if (Mode == NValueSource.Constant)
		{
			return new ConstantValueResampler<float>(ConstantValue, Extent.EffectivelyInfinite, -9999f);
		}
		if (Mode == NValueSource.ClassificationLayer)
		{
			ClassificationLayer associatedClassificationLayer = AssociatedClassificationLayer;
			if (associatedClassificationLayer == null)
			{
				if (string.IsNullOrEmpty(ClassificationLayerName))
				{
					AddLoadError(MessageCodes.GetNoClassifierAssociated(Name));
				}
				else
				{
					AddLoadError(MessageCodes.GetAssociatedClassNotFound(Name, ClassificationLayerName));
				}
				return NoDataResampler;
			}
			Dictionary<int, float> tryBuildClassFloatMap = TryBuildClassFloatMap();
			if (tryBuildClassFloatMap == null)
			{
				return NoDataResampler;
			}
			return new DiscreteValueInterpretationResampler(associatedClassificationLayer, tryBuildClassFloatMap, -9999f);
		}
		_ = Mode;
		_ = 2;
		throw new NotImplementedException();
	}

	public void Resample(ResampleArgs args, Memory<float> outData)
	{
		Resampler.Resample(args, outData);
	}

	public void SamplePoints(Memory<Point> points, Memory<float> outData, SamplingParams sampleParams = default(SamplingParams))
	{
		Resampler.SamplePoints(points, outData, sampleParams);
	}

	public NValueSignature GetSignature()
	{
		NValueSignature nValueSignature = new NValueSignature();
		nValueSignature.Name = Name;
		nValueSignature.Mode = Mode;
		if (Mode == NValueSource.Constant)
		{
			nValueSignature.ConstantValue = ConstantValue;
		}
		else
		{
			if (Mode == NValueSource.ImportedRaster)
			{
				throw new NotSupportedException();
			}
			if (Mode != NValueSource.ClassificationLayer)
			{
				throw new NotImplementedException();
			}
			nValueSignature.InterpretationTableHash = InterpretationTable.GetDataHash();
			nValueSignature.ClassificationLayerName = ClassificationLayerName;
			ClassificationLayer associatedClassificationLayer = AssociatedClassificationLayer;
			if (associatedClassificationLayer != null)
			{
				nValueSignature.ClassificationSignature = associatedClassificationLayer.GetSignature();
			}
		}
		return nValueSignature;
	}
}
