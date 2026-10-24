using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using Geospatial.DataStructures;
using Geospatial.GDALAssist;
using H5Assist.VB;
using Microsoft.VisualBasic.CompilerServices;
using RasMapperLib.Utilities;
using Utility.Extensions;

namespace RasMapperLib;

public class FeatureLayer : Layer, IFeatureLayer
{
	public const string sFeatureID = "FID";

	internal const string sFeature = "Feature";

	public const string sName = "Name";

	protected const string TypeXY = "XY";

	protected const string TypeXYZ = "XYZ";

	protected const string TypeXYZM = "XYZM";

	protected const string XMLTypeAttr = "Type";

	protected const string XMLNameAttr = "Name";

	protected const string DTNameColumn = "Name";

	protected const string sIsDefaultName = "IsDefaultName";

	private DataTable _dt;

	private DateTime _featureTime;

	private DateTime _sourceFileTimestampOnLoad;

	private long _featureChangedCount;



	private int _suppressTableReloadCount;

	private int _suppressFeatureChanged;

	protected Feature _hightlightFeature;

	protected int _hightlightFeatureRow;

	private FileSystemWatcher _watcher;

	private bool _fileUpToDate;

	private Dictionary<string, List<Action<DataRow>>> _columnChangedActions;

	private DatatableLoadManager _dtManager;

	private List<Tuple<string, Type>> _dynamicSHPColumns;

	private Dictionary<string, bool> _columnGroupDefaultVisibilities;

	protected SpatialIndex<int> _spatialIndex;

	private readonly object _loadFeatureTableLock;

	public const string sFeatureParametersGroup = "Feature Parameters";

	protected DataColumnGroup _featureColumnGroup;

	public const string sDynamicColumnsGroup = "User Added Columns";

	protected DataColumnGroup _dynamicColumnsGroup;

	private static List<Type> _typeList = new List<Type>(new Type[6]
	{
		typeof(Point),
		typeof(MultiPoint),
		typeof(Polyline),
		typeof(Polygon),
		typeof(Tin),
		typeof(MeshFV2D)
	});

	public string LayerName => Name;

	public virtual string FeatureColumnName => "Feature";

	public virtual Predicate<DataRow> FeatureRowFilter => null;

	public DatatableLoadManager DatatableManager => _dtManager;

	public override string DefaultLayerName { get; }

	public virtual bool FeaturesRequireUniqueNames { get; set; }

	internal virtual bool AllowZAndM { get; set; }

	public virtual bool CanAddOrRemoveAttributeColumns => SourceFilename.EndsWith(".shp");

	public SelectedFeatureCollection SelectedFeatures { get; set; }

	public override Extent Extent
	{
		get
		{
			if (_extent == null)
			{
				ComputeExtent();
			}
			return _extent;
		}
	}

	public virtual bool AutoUpdateOnSourceFileChange => true;

	public virtual bool CanExportAsShapefile { get; }

	public virtual bool CanFeaturesBeCopied { get; }

	public virtual bool CanExportAsPoints { get; }

	public virtual bool CanLabelFeatures { get; }

	public override bool CanEdit
	{
		get
		{
			if (this is IGeometryLayer)
			{
				return !((IGeometryLayer)this).Geometry.IsResultFile;
			}
			return true;
		}
	}

	public override bool CanMoveToAnotherLevel
	{
		get
		{
			if (this is IGeometryLayer)
			{
				return false;
			}
			return true;
		}
	}

	public override bool CanRename => !(this is IGeometryLayer);

	public override bool CanRemove => !(this is IGeometryLayer);

	public virtual HashSet<string> PasteAttributeBlackList => new string[2] { "Feature", "FID" }.ToHashSet();

	protected bool MouseHighlightEnabled { get; set; }

	public virtual string SimpleFeatureTypeName { get; set; }

	public event EventHandler<FeatureChangedEventArgs> FeatureChanged;

	public event EventHandler<FeatureChangedEventArgs> FeatureChanging;

	public event EventHandler FeatureTableLoaded;

	public FeatureLayer(string name)
		: base(name)
	{
		_dt = null;
		_featureChangedCount = 0L;
		_suppressTableReloadCount = 0;
		_suppressFeatureChanged = 0;
		_hightlightFeature = null;
		_hightlightFeatureRow = -1;
		_columnChangedActions = new Dictionary<string, List<Action<DataRow>>>();
		_dtManager = new DatatableLoadManager();
		_dynamicSHPColumns = new List<Tuple<string, Type>>();
		_columnGroupDefaultVisibilities = new Dictionary<string, bool>();
		DefaultLayerName = "Feature Layer";
		FeaturesRequireUniqueNames = false;
		AllowZAndM = true;
		SelectedFeatures = new SelectedFeatureCollection();
		CanExportAsShapefile = true;
		CanFeaturesBeCopied = true;
		CanExportAsPoints = true;
		CanLabelFeatures = true;
		MouseHighlightEnabled = true;
		SimpleFeatureTypeName = "Feature";
		_loadFeatureTableLock = RuntimeHelpers.GetObjectValue(new object());
		_featureColumnGroup = new DataColumnGroup("Feature Parameters", isVisible: false);
		_dynamicColumnsGroup = new DataColumnGroup("User Added Columns", isVisible: true);
	}

	public FeatureLayer(string name, bool forwardEditRequestsToParent)
		: base(name, forwardEditRequestsToParent)
	{
		_dt = null;
		_featureChangedCount = 0L;
		_suppressTableReloadCount = 0;
		_suppressFeatureChanged = 0;
		_hightlightFeature = null;
		_hightlightFeatureRow = -1;
		_columnChangedActions = new Dictionary<string, List<Action<DataRow>>>();
		_dtManager = new DatatableLoadManager();
		_dynamicSHPColumns = new List<Tuple<string, Type>>();
		_columnGroupDefaultVisibilities = new Dictionary<string, bool>();
		DefaultLayerName = "Feature Layer";
		FeaturesRequireUniqueNames = false;
		AllowZAndM = true;
		SelectedFeatures = new SelectedFeatureCollection();
		CanExportAsShapefile = true;
		CanFeaturesBeCopied = true;
		CanExportAsPoints = true;
		CanLabelFeatures = true;
		MouseHighlightEnabled = true;
		SimpleFeatureTypeName = "Feature";
		_loadFeatureTableLock = RuntimeHelpers.GetObjectValue(new object());
		_featureColumnGroup = new DataColumnGroup("Feature Parameters", isVisible: false);
		_dynamicColumnsGroup = new DataColumnGroup("User Added Columns", isVisible: true);
	}

	public FeatureLayer(string name, string filename)
		: base(name, filename)
	{
		_dt = null;
		_featureChangedCount = 0L;
		_suppressTableReloadCount = 0;
		_suppressFeatureChanged = 0;
		_hightlightFeature = null;
		_hightlightFeatureRow = -1;
		_columnChangedActions = new Dictionary<string, List<Action<DataRow>>>();
		_dtManager = new DatatableLoadManager();
		_dynamicSHPColumns = new List<Tuple<string, Type>>();
		_columnGroupDefaultVisibilities = new Dictionary<string, bool>();
		DefaultLayerName = "Feature Layer";
		FeaturesRequireUniqueNames = false;
		AllowZAndM = true;
		SelectedFeatures = new SelectedFeatureCollection();
		CanExportAsShapefile = true;
		CanFeaturesBeCopied = true;
		CanExportAsPoints = true;
		CanLabelFeatures = true;
		MouseHighlightEnabled = true;
		SimpleFeatureTypeName = "Feature";
		_loadFeatureTableLock = RuntimeHelpers.GetObjectValue(new object());
		_featureColumnGroup = new DataColumnGroup("Feature Parameters", isVisible: false);
		_dynamicColumnsGroup = new DataColumnGroup("User Added Columns", isVisible: true);
	}

	public FeatureLayer(string name, DataTable useFeatureTable)
		: base(name)
	{
		_dt = null;
		_featureChangedCount = 0L;
		_suppressTableReloadCount = 0;
		_suppressFeatureChanged = 0;
		_hightlightFeature = null;
		_hightlightFeatureRow = -1;
		_columnChangedActions = new Dictionary<string, List<Action<DataRow>>>();
		_dtManager = new DatatableLoadManager();
		_dynamicSHPColumns = new List<Tuple<string, Type>>();
		_columnGroupDefaultVisibilities = new Dictionary<string, bool>();
		DefaultLayerName = "Feature Layer";
		FeaturesRequireUniqueNames = false;
		AllowZAndM = true;
		SelectedFeatures = new SelectedFeatureCollection();
		CanExportAsShapefile = true;
		CanFeaturesBeCopied = true;
		CanExportAsPoints = true;
		CanLabelFeatures = true;
		MouseHighlightEnabled = true;
		SimpleFeatureTypeName = "Feature";
		_loadFeatureTableLock = RuntimeHelpers.GetObjectValue(new object());
		_featureColumnGroup = new DataColumnGroup("Feature Parameters", isVisible: false);
		_dynamicColumnsGroup = new DataColumnGroup("User Added Columns", isVisible: true);
		_dt = useFeatureTable;
	}

	~FeatureLayer()
	{
		if (_watcher != null)
		{
			_watcher.Changed -= OnSourceFileChanged;
		}
	}

	public void ValidateSelectedFeatures()
	{
		SelectedFeatures.RemoveIfGreaterThan(checked(FeatureCount() - 1));
	}

	public virtual DataTable FeatureTable()
	{
		if (this is IVirtualFeatureLayer)
		{
			return GetFeatureTableVirtual();
		}
		if (_dt == null)
		{
			object loadFeatureTableLock = _loadFeatureTableLock;
			ObjectFlowControl.CheckForSyncLockOnValueType(loadFeatureTableLock);
			bool lockTaken = false;
			try
			{
				Monitor.Enter(loadFeatureTableLock, ref lockTaken);
				if (_dt != null)
				{
					return _dt;
				}
				using (new FeatureChangedSuppressor(this))
				{
					using (new FeatureTableReloadSuppressor(this))
					{
						_sourceFileTimestampOnLoad = SourceFileDate;
						InitializeEmptyFeatureTable();
						try
						{
							LoadFeatureTable();
						}
						catch (Exception ex)
						{
							ProjectData.SetProjectError(ex);
							Exception ex2 = ex;
							ErrorMessage = "Could not read features from '" + SourceFilename + "' - " + ex2.Message;
							ProjectData.ClearProjectError();
						}
						if (_watcher != null)
						{
							_watcher.Changed -= OnSourceFileChanged;
						}
						if (!string.IsNullOrEmpty(SourceFilename) && SourceFileExists && SharedData.ListenForSourceFileChanges)
						{
							_watcher = new FileSystemWatcher();
							_watcher.Path = System.IO.Path.GetDirectoryName(SourceFilename);
							_watcher.Filter = System.IO.Path.GetFileName(SourceFilename);
							_watcher.EnableRaisingEvents = true;
							_watcher.Changed += OnSourceFileChanged;
							_watcher.Created += OnSourceFileChanged;
							_watcher.Deleted += OnSourceFileChanged;
						}
						ComputeExtent();
						_fileUpToDate = true;
					}
				}
				FeatureTableLoadedInternal();
			}
			finally
			{
				if (lockTaken)
				{
					Monitor.Exit(loadFeatureTableLock);
				}
			}
		}
		else
		{
			ReloadFeatureTableIfNecessary();
		}
		return _dt;
	}

	DataTable IFeatureLayer.FeatureTable()
	{
		//ILSpy generated this explicit interface implementation from .override directive in FeatureTable
		return this.FeatureTable();
	}

	protected override void OnSourceFilenameChanged(string oldName)
	{
		base.OnSourceFilenameChanged(oldName);
	}

	public void SuppressTableReload(bool boolValue)
	{
		checked
		{
			if (boolValue)
			{
				_suppressTableReloadCount++;
			}
			else
			{
				_suppressTableReloadCount--;
			}
		}
	}

	public void SuppressFeatureChanged(bool boolValue)
	{
		checked
		{
			if (boolValue)
			{
				_suppressFeatureChanged++;
			}
			else
			{
				_suppressFeatureChanged--;
			}
		}
	}

	private DataTable GetFeatureTableVirtual()
	{
		FeatureLayer sourceLayer = ((IVirtualFeatureLayer)this).SourceLayer;
		if (sourceLayer.FeatureTable() == _dt)
		{
			return _dt;
		}
		LoadFeatureTableFrom(sourceLayer);
		return _dt;
	}

	private void OnSourceFileChanged(object src, FileSystemEventArgs e)
	{
		_ = _fileUpToDate;
		_fileUpToDate = false;
	}

	private void FeatureTableLoadedInternal()
	{
		OnFeatureTableLoaded();
		FeatureTableLoaded?.Invoke(this, new EventArgs());
		FeatureTable().ColumnChanged += FeatureAttributeChanged;
	}

	protected virtual void OnFeatureTableLoaded()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				ResetFeatureAttributes(i);
			}
		}
	}

	public void RegisterAttributeChangedAction(string columnName, Action<DataRow> action)
	{
		if (!_columnChangedActions.ContainsKey(columnName))
		{
			_columnChangedActions[columnName] = new List<Action<DataRow>>();
		}
		_columnChangedActions[columnName].Add(action);
	}

	private void FeatureAttributeChanged(object ob, DataColumnChangeEventArgs ev)
	{
		List<Action<DataRow>> localValue = null;
		if (!_columnChangedActions.TryGetValue(ev.Column.ColumnName, out localValue))
		{
			return;
		}
		foreach (Action<DataRow> action in localValue)
		{
			action(ev.Row);
		}
	}

	public void ResetFeatureTable()
	{
		ClearFeatureTable();
		InitializeEmptyFeatureTable();
	}

	public void InitializeEmptyFeatureTable()
	{
		_dt = new DataTable();
		using (new FeatureTableReloadSuppressor(this))
		{
			LoadDefaultAttributeColumnsBase();
		}
	}

	protected virtual void LoadDefaultAttributeColumns()
	{
		AddAttributeColumn(_dt, "FID", typeof(int), null, DataColumnViewState.GrayedOut);
		AddAttributeColumn(_dt, "Feature", typeof(Feature), _featureColumnGroup, DataColumnViewState.GrayedOut);
	}

	private void LoadDefaultAttributeColumnsBase()
	{
		LoadDefaultAttributeColumns();
		RegisterDefaultColumnGroupVisibilities();
	}

	public override bool LayerHasData(H5Reader hr = null)
	{
		bool result = false;
		if (!IsFeatureTableLoaded())
		{
			if (!string.IsNullOrEmpty(SourceFilename))
			{
				string left = System.IO.Path.GetExtension(SourceFilename).ToLowerInvariant();
				if (!File.Exists(SourceFilename))
				{
					ErrorMessage = SourceFileDoesntExistMessage;
					return false;
				}
				if (Operators.CompareString(left, ".shp", TextCompare: false) == 0)
				{
					result = File.Exists(SourceFilename);
				}
				else if (this is IGeometryLayer && (Operators.CompareString(left, ".hdf", TextCompare: false) == 0 || Operators.CompareString(left, ".h5", TextCompare: false) == 0))
				{
					bool condition = false;
					if (hr == null)
					{
						condition = true;
						hr = new H5Reader(SourceFilename);
					}
					result = ((!(this is IVirtualFeatureLayer)) ? GeometryLayerExtensions.HasFeatures((IGeometryLayer)this, hr) : GeometryLayerExtensions.HasFeatures(new GeometryLayerExtensions.VirtualGeometryWrapper(this), ((IVirtualFeatureLayer)this).CountAttributeName, hr));
					if (condition)
					{
						hr.Dispose();
					}
				}
			}
			return result;
		}
		return FilteredFeatureCount() > 0;
	}

	protected virtual void LoadFeatureTable()
	{
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return;
		}
		if (!File.Exists(SourceFilename))
		{
			ErrorMessage = SourceFileDoesntExistMessage;
			return;
		}
		string left = System.IO.Path.GetExtension(SourceFilename).ToLowerInvariant();
		if (Operators.CompareString(left, ".shp", TextCompare: false) == 0)
		{
			ShapefileStorage.LoadFeatureTable(SourceFilename, this);
		}
		else if (this is IGeometryLayer && (Operators.CompareString(left, ".hdf", TextCompare: false) == 0 || Operators.CompareString(left, ".h5", TextCompare: false) == 0))
		{
			using (H5Reader hr = new H5Reader(SourceFilename))
			{
				((IGeometryLayer)this).HDFLoadFeatureTable(hr);
			}
		}
	}

	public virtual bool SaveFeatureTable()
	{
		bool result = SaveFeatureTableInternal();
		_fileUpToDate = true;
		ErrorMessage = "";
		return result;
	}

	protected virtual bool SaveFeatureTableInternal()
	{
		if (!IsFeatureTableLoaded())
		{
			return false;
		}
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return false;
		}
		string left = System.IO.Path.GetExtension(SourceFilename).ToLower();
		using (new FeatureTableReloadSuppressor(this))
		{
			if (Operators.CompareString(left, ".shp", TextCompare: false) == 0)
			{
				string sourceFilename = SourceFilename;
				string summary = "";
				return ShapefileStorage.WriteShapefile(this, sourceFilename, ref summary);
			}
			if (this is IGeometryLayer && (Operators.CompareString(left, ".hdf", TextCompare: false) == 0 || Operators.CompareString(left, ".h5", TextCompare: false) == 0))
			{
				using (H5Writer hw = new H5Writer(SourceFilename))
				{
					((IGeometryLayer)this).HDFSaveFeatureTable(hw);
				}
				return true;
			}
		}
		return false;
	}

	public void ClearFeatureTable()
	{
		_dt = null;
		_dtManager = new DatatableLoadManager();
		_featureTime = DateTime.Now;
		Interlocked.Increment(ref _featureChangedCount);
		_sourceFileTimestampOnLoad = DateTime.MinValue;
		_fileUpToDate = true;
		_extent = null;
		ErrorMessage = "";
		ClearSelection();
		OnFeatureTableCleared();
	}

	public bool IsFeatureTableLoaded()
	{
		if (_dt == null)
		{
			return false;
		}
		return true;
	}

	private void LoadExternalFeatureTable(object sender, EventArgs ev)
	{
		if (sender is FeatureLayer flyr)
		{
			LoadFeatureTableFrom(flyr);
		}
	}

	private void LoadFeatureTableFrom(FeatureLayer flyr)
	{
		_dt = flyr.FeatureTable();
		using (new FeatureTableReloadSuppressor(this))
		{
			LoadDefaultAttributeColumnsBase();
			LoadFeatureTable();
		}
	}

	protected virtual void ReloadFeatureTableIfNecessary()
	{
		if (ShouldFeatureTableReload())
		{
			ClearFeatureTable();
			FeatureTable();
		}
	}

	protected virtual bool ShouldFeatureTableReload()
	{
		if (_fileUpToDate)
		{
			return false;
		}
		if (!AutoUpdateOnSourceFileChange)
		{
			return false;
		}
		if (_suppressTableReloadCount > 0)
		{
			return false;
		}
		if (this is IVirtualFeatureLayer)
		{
			return false;
		}
		if (string.IsNullOrEmpty(SourceFilename))
		{
			return false;
		}
		if (IsEditingData)
		{
			return false;
		}
		return true;
	}

	protected virtual void OnFeatureTableCleared()
	{
		_extent = null;
		_spatialIndex = null;
	}

	public bool Save()
	{
		return SaveFeatureTable();
	}

	public void AddManagedAttributeColumn(string columnName, string diskColumnName, Type columnType, DataColumnGroup group = null, DataColumnViewState viewState = DataColumnViewState.FreeRange, bool outputMessageIfNotFound = false)
	{
		AddAttributeColumn(columnName, columnType, group, viewState);
		_dtManager.Add(columnName, diskColumnName, columnType, outputMessageIfNotFound, group, viewState);
	}

	protected void AddManagedWriteOnlyAttributeColumn<T>(string diskColumnName, Func<int, T> writeValue) where T : struct
	{
		_dtManager.AddWriteOnly(diskColumnName, writeValue);
	}

	protected void ReadManagedColumns(H5Reader hr, string datasetName)
	{
		DatatableManager.Read(hr, datasetName, FeatureTable());
	}

	protected void ReadManagedColumnMetadata(H5Reader hr, string datasetName)
	{
		DatatableManager.ReadColumns(hr, datasetName);
		foreach (DatatableLoadManager.MappedColumnDefinition column in DatatableManager.ColumnList)
		{
			AddAttributeColumn(column.InMemoryName, column.Type, null, column.ViewState);
		}
	}

	protected void WriteManagedColumns(H5Writer hw, string datasetName)
	{
		DatatableManager.Write(hw, datasetName, FeatureTable());
	}

	protected void WriteManagedColumnMetadata(H5Writer hw, string datasetName)
	{
		DatatableManager.WriteColumns(hw, datasetName);
	}

	public int AddAttributeColumn(string columnName, Type columnType, DataColumnGroup group = null, DataColumnViewState viewState = DataColumnViewState.FreeRange)
	{
		if (FeaturesRequireUniqueNames && Operators.CompareString(columnName, "Name", TextCompare: false) == 0 && viewState == DataColumnViewState.FreeRange)
		{
			viewState = DataColumnViewState.GrayedOut;
		}
		return AddAttributeColumn(FeatureTable(), columnName, columnType, group, viewState);
	}

	public static int AddAttributeColumn(DataTable dt, string columnName, Type columnType, DataColumnGroup group = null, DataColumnViewState viewState = DataColumnViewState.FreeRange)
	{
		lock (dt)
		{
			int indexOf = dt.Columns.IndexOf(columnName);
			if (indexOf != -1)
			{
				return indexOf;
			}
			CustomDataColumn customDataColumn = new CustomDataColumn(columnName, columnType, viewState, group);
			dt.Columns.Add(customDataColumn);
			return customDataColumn.Ordinal;
		}
	}

	public void AddAutoNameColumn()
	{
		AddAttributeColumn("Name", typeof(string));
		FeaturesRequireUniqueNames = true;
		FeatureChanged += [SpecialName] (object ob, FeatureChangedEventArgs ev) =>
		{
			if (ev.ChangeType == FeatureChangedType.FeatureAdded)
			{
				foreach (int featureIndicesChanged in ev.FeatureIndicesChanged)
				{
					SetUniqueFeatureName(featureIndicesChanged);
				}
			}
		};
	}

	public void RemoveAttributeColumn(string columnName)
	{
		FeatureTable().Columns.Remove(columnName);
	}

	public void AppendFeatureTable(FeatureLayer ftLayer)
	{
		DataTable dataTable = ftLayer.FeatureTable();
		DataTable dataTable2 = FeatureTable();
		List<KeyValuePair<int, int>> intForIntList = new List<KeyValuePair<int, int>>();
		checked
		{
			int count = dataTable.Columns.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (dataTable.Columns[i].ReadOnly)
				{
					continue;
				}
				int indexOf = dataTable2.Columns.IndexOf(dataTable.Columns[i].ColumnName);
				if (indexOf > -1 && !dataTable2.Columns[indexOf].ReadOnly)
				{
					if (dataTable2.Columns[indexOf].DataType == dataTable.Columns[i].DataType)
					{
						intForIntList.Add(new KeyValuePair<int, int>(i, indexOf));
					}
					else if (dataTable2.Columns[indexOf].DataType == typeof(float) && dataTable.Columns[i].DataType == typeof(double))
					{
						intForIntList.Add(new KeyValuePair<int, int>(i, indexOf));
					}
					else if (dataTable2.Columns[indexOf].DataType == typeof(double) && dataTable.Columns[i].DataType == typeof(float))
					{
						intForIntList.Add(new KeyValuePair<int, int>(i, indexOf));
					}
				}
			}
			int count2 = ftLayer.FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				Feature feature = ftLayer.Feature(j);
				DataRow dataRow = AddFeature(feature);
				DataRow dataRow2 = ftLayer.FeatureRow(j);
				foreach (KeyValuePair<int, int> localValue in intForIntList)
				{
					dataRow[localValue.Value] = RuntimeHelpers.GetObjectValue(dataRow2[localValue.Key]);
				}
			}
		}
	}

	public void CopyFeaturesAndAttributes(int fid, FeatureLayer copyFromLayer)
	{
		CopyFeaturesAndAttributes(new int[1] { fid }, copyFromLayer);
	}

	public void CopyFeaturesAndAttributes(IList<int> fids, FeatureLayer copyFromLayer)
	{
		DataTable dataTable = copyFromLayer.FeatureTable();
		checked
		{
			int count = fids.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				int fidIndex = fids[i];
				Feature feature = copyFromLayer.Feature(fidIndex);
				DataRow dataRow = AddFeature(feature);
				DataRow dataRow2 = dataTable.Rows[fidIndex];
				foreach (DataColumn column in dataTable.Columns)
				{
					if (!column.ReadOnly)
					{
						int columnIndex = AddAttributeColumn(column.ColumnName, column.DataType);
						dataRow[columnIndex] = RuntimeHelpers.GetObjectValue(dataRow2[column.Ordinal]);
					}
				}
			}
		}
	}

	public void CopyFeaturesAndAttributes(FeatureLayer copyFromLayer)
	{
		CopyFeaturesAndAttributes(Enumerable.Range(0, copyFromLayer.FeatureCount()).ToList(), copyFromLayer);
	}

	public void CopyFeaturesAndAttributesPreserveExistingRowsAndColumns(FeatureLayer copyFromLayer, Dictionary<DataRow, DataRow> featureRowMapping)
	{
		Dictionary<Feature, int> intByFeature = new Dictionary<Feature, int>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				intByFeature.Add(Feature(i), i);
			}
			HashSet<string> toHashSet = ColumnNames(typeof(int), typeof(bool), typeof(double), typeof(float), typeof(string), typeof(short), typeof(byte), typeof(decimal)).ToHashSet();
			DataTable dataTable = copyFromLayer.FeatureTable();
			int count2 = copyFromLayer.FeatureCount() - 1;
			for (int j = 0; j <= count2; j++)
			{
				Feature feature = copyFromLayer.Feature(j);
				DataRow dataRow = dataTable.Rows[j];
				DataRow dataRow2 = ((!featureRowMapping.ContainsKey(dataRow)) ? AddFeature(feature) : featureRowMapping[dataRow]);
				int count3 = dataTable.Columns.Count - 1;
				for (int k = 0; k <= count3; k++)
				{
					DataColumn dataColumn = dataTable.Columns[k];
					if (toHashSet.Contains(dataColumn.ColumnName))
					{
						_ = FeatureTable().Columns[dataColumn.ColumnName].Ordinal;
						dataRow2[dataColumn.ColumnName] = RuntimeHelpers.GetObjectValue(dataRow[dataColumn.ColumnName]);
					}
				}
				dataRow2["Feature"] = feature;
			}
		}
	}

	public virtual void CopyTo<T>(T fLayer) where T : FeatureLayer
	{
		fLayer.Name = Name;
		fLayer._dt = FeatureTable().Copy();
	}

	protected void OverrideFeatureTable(DataTable dt)
	{
		_dt = dt;
	}

	public virtual void DeleteSourceFiles()
	{
		if (string.IsNullOrEmpty(SourceFilename) || !File.Exists(SourceFilename))
		{
			return;
		}
		string left = System.IO.Path.GetExtension(SourceFilename).ToLower();
		try
		{
			if (Operators.CompareString(left, ".shp", TextCompare: false) == 0)
			{
				ShapefileStorage.Delete(SourceFilename);
			}
			else if (Operators.CompareString(left, ".vrt", TextCompare: false) == 0)
			{
				string[] vRTSources = GDALRaster.GetVRTSources(SourceFilename);
				foreach (string path in vRTSources)
				{
					try
					{
						if (File.Exists(path))
						{
							File.Delete(path);
						}
					}
					catch (Exception ex)
					{
						ProjectData.SetProjectError(ex);
						Exception ex2 = ex;
						ProjectData.ClearProjectError();
					}
				}
			}
			else if (File.Exists(SourceFilename))
			{
				File.Delete(SourceFilename);
			}
		}
		catch (Exception ex3)
		{
			ProjectData.SetProjectError(ex3);
			Exception ex4 = ex3;
			ProjectData.ClearProjectError();
		}
	}

	public List<string> ColumnNames(params Type[] types)
	{
		List<string> stringList = new List<string>();
		FeatureTable();
		foreach (DataColumn column in _dt.Columns)
		{
			if (types.Count() == 0 || Enumerable.Contains(types, column.DataType))
			{
				stringList.Add(column.ColumnName);
			}
		}
		return stringList;
	}

	public virtual List<DataColumnGroup> GetUniqueColumnGroups()
	{
		HashSet<DataColumnGroup> dataColumnGroups = new HashSet<DataColumnGroup>();
		bool condition = false;
		if (_dt == null && this is IVirtualFeatureLayer)
		{
			if (((IVirtualFeatureLayer)this).SourceLayer._dt == null)
			{
				condition = true;
				((IVirtualFeatureLayer)this).SourceLayer.InitializeEmptyFeatureTable();
			}
			_dt = ((IVirtualFeatureLayer)this).SourceLayer._dt;
		}
		if (_dt == null)
		{
			condition = true;
			InitializeEmptyFeatureTable();
		}
		foreach (DataColumn column in _dt.Columns)
		{
			if (column is CustomDataColumn { Group: not null } customDataColumn)
			{
				dataColumnGroups.Add(customDataColumn.Group);
			}
		}
		if (condition)
		{
			ClearFeatureTable();
			if (this is IVirtualFeatureLayer)
			{
				((IVirtualFeatureLayer)this).SourceLayer._dt = null;
			}
		}
		return dataColumnGroups.ToList();
	}

	public virtual List<DataColumnGroup> GetUniqueColumnGroupCopies()
	{
		List<DataColumnGroup> uniqueColumnGroups = GetUniqueColumnGroups();
		checked
		{
			int count = uniqueColumnGroups.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				uniqueColumnGroups[i] = uniqueColumnGroups[i].GetCopy();
			}
			return uniqueColumnGroups;
		}
	}

	private void RegisterDefaultColumnGroupVisibilities()
	{
		List<DataColumnGroup> uniqueColumnGroups = ((!(this is IVirtualFeatureLayer)) ? GetUniqueColumnGroups() : GetUniqueColumnGroupCopies());
		foreach (DataColumnGroup uniqueColumnGroup in uniqueColumnGroups)
		{
			_columnGroupDefaultVisibilities[uniqueColumnGroup.DisplayName] = uniqueColumnGroup.IsVisible;
		}
	}

	public DataRow AddFeature(Feature feature)
	{
		return InsertFeature(FeatureCount(), feature);
	}

	public DataRow InsertFeature(int fID, Feature feature)
	{
		if (SharedData.DebugEditingTools)
		{
			_ = feature is IEditableFeature;
		}
		FeatureChangingInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureAdded));
		DataRow result = InsertFeatureInternal(fID, feature);
		FeatureChangedInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureAdded));
		return result;
	}

	public List<DataRow> AddFeatureRange(List<Feature> ftList)
	{
		FeatureCount();
		List<DataRow> dataRows = new List<DataRow>();
		IEnumerable<int> enumerable = Enumerable.Range(FeatureCount(), ftList.Count);
		FeatureChangingInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureAdded));
		int intervalIndex = 0;
		foreach (int enumerable2 in enumerable)
		{
			Feature feature = ftList[intervalIndex];
			if (SharedData.DebugEditingTools)
			{
				_ = feature is IEditableFeature;
			}
			dataRows.Add(InsertFeatureInternal(enumerable2, feature));
			intervalIndex = checked(intervalIndex + 1);
		}
		FeatureChangedInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureAdded));
		return dataRows;
	}

	private DataRow InsertFeatureInternal(int fID, Feature feature)
	{
		DataTable dataTable = FeatureTable();
		DataRow dataRow = dataTable.NewRow();
		dataRow[FeatureColumnName] = feature;
		dataRow["FID"] = fID;
		dataTable.Rows.InsertAt(dataRow, fID);
		return dataRow;
	}

	private DataRow InsertRowEventlessInternal(int fID, IList<object> rowObjs)
	{
		DataTable dataTable = FeatureTable();
		DataRow dataRow = dataTable.NewRow();
		checked
		{
			int count = rowObjs.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				dataRow[i] = RuntimeHelpers.GetObjectValue(rowObjs[i]);
			}
			dataTable.Rows.InsertAt(dataRow, fID);
			return dataRow;
		}
	}

	public Dictionary<int, DataRow> InsertFeatureRange(Dictionary<int, Feature> fidFeatureMap)
	{
		IEnumerable<int> enumerable = from kvp in fidFeatureMap
			select kvp.Key into f
			orderby f
			select f;
		Dictionary<int, DataRow> dataRowByInt = new Dictionary<int, DataRow>();
		FeatureChangingInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureAdded));
		foreach (int enumerable2 in enumerable)
		{
			Feature feature = fidFeatureMap[enumerable2];
			dataRowByInt.Add(enumerable2, InsertFeatureInternal(enumerable2, feature));
		}
		FeatureChangedInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureAdded));
		return dataRowByInt;
	}

	public void InsertRowEventless(int fid, IList<object> data)
	{
		InsertRowEventlessInternal(fid, data);
	}

	public void InsertDatarowRange(Dictionary<int, IList<object>> rowMap)
	{
		IEnumerable<int> enumerable = from kvp in rowMap
			select kvp.Key into f
			orderby f
			select f;
		FeatureChangingInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureAdded));
		foreach (int enumerable2 in enumerable)
		{
			InsertRowEventlessInternal(enumerable2, rowMap[enumerable2]);
		}
		FeatureChangedInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureAdded));
	}

	public List<object> GetValuesFromColumn(string colName)
	{
		List<object> valuesFromColumn = new List<object>();
		DataTable dataTable = FeatureTable();
		int ordinal = -1;
		checked
		{
			int count = dataTable.Columns.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				if (Operators.CompareString(dataTable.Columns[i].ColumnName, colName, TextCompare: false) == 0)
				{
					ordinal = i;
					break;
				}
			}
			if (ordinal == -1)
			{
				return valuesFromColumn;
			}
			int count2 = dataTable.Rows.Count - 1;
			for (int j = 0; j <= count2; j++)
			{
				DataRow dataRow = dataTable.Rows[j];
				valuesFromColumn.Add(RuntimeHelpers.GetObjectValue(dataRow[ordinal]));
			}
			return valuesFromColumn;
		}
	}

	public DataRow SetFeature(int fID, Feature feature)
	{
		FeatureChangingInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureModified));
		if (SharedData.DebugEditingTools)
		{
			_ = feature is IEditableFeature;
		}
		DataRow result = SetFeatureEventless(fID, feature);
		FeatureChangedInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureModified));
		return result;
	}

	private DataRow SetFeatureEventless(int fid, Feature ft)
	{
		DataRow dataRow = _dt.Rows[fid];
		dataRow["FID"] = fid;
		dataRow[FeatureColumnName] = ft;
		return dataRow;
	}

	public void DeleteFeature(int fID)
	{
		Feature ftDeleted = Feature(fID);
		FeatureChangingInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureDeleted, ftDeleted));
		_dt.Rows.RemoveAt(fID);
		ValidateSelectedFeatures();
		FeatureChangedInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureDeleted, ftDeleted));
	}

	public void DeleteFeatureRange(IEnumerable<int> fIDs)
	{
		IEnumerable<int> enumerable = fIDs.OrderByDescending([SpecialName] (int f) => f);
		List<Feature> featureDeletedIds = new List<Feature>();
		_ = SharedData.DebugEditingTools;
		FeatureChangingInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureDeleted, featureDeletedIds));
		foreach (int enumerable2 in enumerable)
		{
			featureDeletedIds.Add(Feature(enumerable2));
			_dt.Rows.RemoveAt(enumerable2);
		}
		ValidateSelectedFeatures();
		FeatureChangedInternal(new FeatureChangedEventArgs(enumerable, FeatureChangedType.FeatureDeleted, featureDeletedIds));
	}

	public void DeleteFeatureRange(int start, int count)
	{
		List<Feature> featureDeletedIds = new List<Feature>();
		List<int> featureDeletedIdsList = new List<int>();
		checked
		{
			for (int i = start + count - 1; i >= start; i += -1)
			{
				featureDeletedIdsList.Add(i);
				featureDeletedIds.Add(Feature(i));
			}
			FeatureChangingInternal(new FeatureChangedEventArgs(featureDeletedIdsList, FeatureChangedType.FeatureDeleted, featureDeletedIds));
			for (int j = start + count - 1; j >= start; j += -1)
			{
				_dt.Rows.RemoveAt(j);
			}
			_ = SharedData.DebugEditingTools;
			ValidateSelectedFeatures();
			FeatureChangedInternal(new FeatureChangedEventArgs(featureDeletedIdsList, FeatureChangedType.FeatureDeleted, featureDeletedIds));
		}
	}

	public void DeleteAllFeatures()
	{
		_ = SharedData.DebugEditingTools;
		checked
		{
			if (_dt != null && _dt.Rows.Count != 0)
			{
				int featureCount = FeatureCount();
				List<Feature> featureDeletedIds = new List<Feature>();
				int count = featureCount - 1;
				for (int i = 0; i <= count; i++)
				{
					featureDeletedIds.Add(Feature(i));
				}
				FeatureChangingInternal(new FeatureChangedEventArgs(Enumerable.Range(0, featureCount - 1), FeatureChangedType.FeatureDeleted, featureDeletedIds));
				for (int j = featureCount - 1; j >= 0; j += -1)
				{
					_dt.Rows.RemoveAt(j);
				}
				ClearSelection();
				FeatureChangedInternal(new FeatureChangedEventArgs(Enumerable.Range(0, featureCount - 1), FeatureChangedType.FeatureDeleted, featureDeletedIds));
			}
		}
	}

	public virtual void BeforeFeatureSave(int fid, IEditableFeature feat)
	{
	}

	public void EnsureImmutableFeature(int fid)
	{
		Feature feature = Feature(fid);
		if (feature is IEditableFeature)
		{
			BeforeFeatureSave(fid, (IEditableFeature)feature);
			SetFeatureEventless(fid, ((IEditableFeature)feature).Save());
		}
	}

	public void EnsureAllFeaturesImmutable()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				EnsureImmutableFeature(i);
			}
		}
	}

	public IEditableFeature EditableFeature(int fid)
	{
		Feature feature = Feature(fid);
		if (feature is IEditableFeature)
		{
			return (IEditableFeature)feature;
		}
		IEditableFeature mutableView = feature.GetMutableView();
		SetFeatureEventless(fid, (Feature)mutableView);
		return mutableView;
	}

	public void NotifyFeatureModifying(List<int> fIDs, Transform transformer)
	{
		FeatureChangingInternal(new FeatureChangedEventArgs(fIDs, FeatureChangedType.FeatureModified, transformer));
	}

	public void NotifyFeatureChanging(FeatureChangedEventArgs ftce)
	{
		FeatureChangingInternal(ftce);
	}

	public void NotifyFeatureModified(int fID)
	{
		FeatureChangedInternal(new FeatureChangedEventArgs(fID, FeatureChangedType.FeatureModified));
	}

	public void NotifyFeatureModified(IList<int> fIDs)
	{
		FeatureChangedInternal(new FeatureChangedEventArgs(fIDs, FeatureChangedType.FeatureModified));
	}

	public void NotifyFeatureModified(List<int> fIDs, Transform transformer)
	{
		FeatureChangedInternal(new FeatureChangedEventArgs(fIDs, FeatureChangedType.FeatureModified, transformer));
	}

	public void NotifyFeatureChanged(FeatureChangedEventArgs ftce)
	{
		FeatureChangedInternal(ftce);
	}

	private void FeatureChangedInternal(FeatureChangedEventArgs args)
	{
		if (_suppressFeatureChanged <= 0)
		{
			OnFeatureChanged(args);
			FeatureChanged?.Invoke(this, args);
			if (this is IVirtualFeatureLayer)
			{
				((IVirtualFeatureLayer)this).SourceLayer.FeatureChangedInternal(args);
			}
		}
	}

	private void FeatureChangingInternal(FeatureChangedEventArgs args)
	{
		OnFeatureChanging(args);
		FeatureChanging?.Invoke(this, args);
		if (this is IVirtualFeatureLayer)
		{
			((IVirtualFeatureLayer)this).SourceLayer.FeatureChangingInternal(args);
		}
	}

	public void ForceFeatureChange(FeatureChangedEventArgs args, bool plotLayers)
	{
		UpdateFeatureTime();
	}

	protected virtual void OnFeatureChanged(FeatureChangedEventArgs args)
	{
		if (args.ChangeType == FeatureChangedType.FeatureAdded && _extent != null)
		{
			foreach (int featureIndicesChanged in args.FeatureIndicesChanged)
			{
				_extent = Vector.MaximumExtent(_extent, Feature(featureIndicesChanged), allowMaxMinValues: false);
			}
		}
		else
		{
			_extent = null;
		}
		UpdateFeatureTime();
		Interlocked.Increment(ref _featureChangedCount);
		if (args.ChangeType != FeatureChangedType.FeatureDeleted)
		{
			foreach (int featureIndicesChanged2 in args.FeatureIndicesChanged)
			{
				ResetFeatureAttributes(featureIndicesChanged2);
			}
		}
		_spatialIndex = null;
	}

	protected virtual void OnFeatureChanging(FeatureChangedEventArgs args)
	{
	}

	protected void UpdateFeatureTime()
	{
		_featureTime = DateTime.Now;
	}

	protected virtual void ResetFeatureAttributes(int fid)
	{
	}

	public virtual string GetFeatureDisplayLabel(int fid)
	{
		string featureName = GetFeatureName(fid);
		if (string.IsNullOrEmpty(featureName))
		{
			return fid.ToString();
		}
		return featureName;
	}

	public virtual string GetFeatureName(int fid)
	{
		if (FeatureTable().Columns.Contains("Name"))
		{
			return FeatureRow(fid)["Name"] as string;
		}
		return fid.ToString();
	}

	public virtual void SetFeatureName(int fid, string newName)
	{
		if (FeatureTable().Columns.Contains("Name"))
		{
			FeatureRow(fid)["Name"] = newName;
		}
	}

	public void SetUniqueFeatureName(int fid)
	{
		SetFeatureName(fid, GetUniqueFeatureName());
	}

	public int FeatureCount()
	{
		return FeatureTable().Rows.Count;
	}

	public int FilteredFeatureCount()
	{
		if (FeatureRowFilter == null)
		{
			return FeatureTable().Rows.Count;
		}
		DataTable dataTable = FeatureTable();
		int ordinal = 0;
		foreach (DataRow row in dataTable.Rows)
		{
			if (FeatureRowFilter(row))
			{
				ordinal = checked(ordinal + 1);
			}
		}
		return ordinal;
	}

	public virtual Feature Feature(int fId)
	{
		DataTable dataTable = FeatureTable();
		if (fId >= dataTable.Rows.Count || fId < 0)
		{
			if (SharedData.DebugMode)
			{
				throw new IndexOutOfRangeException();
			}
			return null;
		}
		Feature feature = dataTable.Rows[fId][FeatureColumnName] as Feature;
		if (feature == null)
		{
			feature = ResolveNullFeature();
		}
		return feature;
	}

	public virtual Feature ResolveNullFeature()
	{
		return null;
	}

	public DataRow FeatureRow(int fID)
	{
		DataTable dataTable = FeatureTable();
		if (fID >= dataTable.Rows.Count || fID < 0)
		{
			if (SharedData.DebugMode)
			{
				throw new IndexOutOfRangeException();
			}
			return null;
		}
		return dataTable.Rows[fID];
	}

	public string GetSequentialFeatureName(string overrideSimpleFeatureTypeName = null)
	{
		checked
		{
			int featureCountTerm = FeatureCount() - 1;
			string simpleFeatureTypeName = SimpleFeatureTypeName;
			if (overrideSimpleFeatureTypeName != null)
			{
				simpleFeatureTypeName = overrideSimpleFeatureTypeName;
			}
			bool condition;
			do
			{
				condition = true;
				featureCountTerm++;
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					if (Operators.CompareString(GetFeatureName(i), simpleFeatureTypeName + " #" + featureCountTerm, TextCompare: false) == 0)
					{
						condition = false;
						break;
					}
				}
			}
			while (!condition);
			return simpleFeatureTypeName + " #" + featureCountTerm;
		}
	}

	public IEnumerable<DataRow> FeatureRows()
	{
		DataTable dataTable = FeatureTable();
		checked
		{
			int count = dataTable.Rows.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return dataTable.Rows[i];
			}
		}
	}

	public DateTime FeaturesTime()
	{
		FeatureTable();
		return _featureTime;
	}

	public long FeatureChangedCount()
	{
		return _featureChangedCount;
	}

	public List<int> FilteredFIDS()
	{
		List<int> intList = new List<int>();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (FeatureRowFilter == null || FeatureRowFilter(FeatureRow(i)))
				{
					intList.Add(i);
				}
			}
			return intList;
		}
	}

	public virtual List<Type> AvailableFeatureTypes()
	{
		return _typeList;
	}

	public List<Type> ActualFeatureTypes()
	{
		return (from ft in Features()
			select ft.GetType()).Distinct().ToList();
	}

	public IEnumerable<Feature> Features()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				yield return Feature(i);
			}
		}
	}

	public IEnumerable<T> FeaturesOfType<T>() where T : Feature
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if (feature is T)
				{
					yield return (T)feature;
				}
			}
		}
	}

	public IEnumerable<T> FeaturesOfExactType<T>() where T : Feature
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = Feature(i);
				if ((object)feature.GetType() == typeof(T))
				{
					yield return (T)feature;
				}
			}
		}
	}

	public bool ColumnMatchesType(int colIdx, Type t)
	{
		if ((object)FeatureTable().Columns[colIdx].DataType == typeof(Point2DCollection))
		{
			return true;
		}
		return false;
	}

	public virtual void ComputeExtent()
	{
		_extent = null;
		if (FilteredFeatureCount() == 0)
		{
			return;
		}
		if (FeatureRowFilter != null)
		{
			List<IExtent> iExtents = new List<IExtent>();
			int ordinal = 0;
			foreach (DataRow featureRow in FeatureRows())
			{
				if (FeatureRowFilter(featureRow))
				{
					iExtents.Add(Feature(ordinal));
				}
				ordinal = checked(ordinal + 1);
			}
			_extent = new Extent(iExtents.ToArray());
		}
		else
		{
			_extent = new Extent(Features().Cast<IExtent>().ToArray());
		}
	}

	public virtual void MD5HashAppend(Hash hash)
	{
		FeatureTable();
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature(i).MD5HashAppend(hash);
			}
		}
	}

	public XmlElement GetLayerUpdateXML(XmlDocument doc)
	{
		XmlElement xmlElement = doc.CreateElement("Layer");
		xmlElement.SetAttribute("Name", Name);
		return xmlElement;
	}

	public void EnsureAllFeaturesHaveUniqueNames()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string featureName = GetFeatureName(i);
				if (string.IsNullOrEmpty(featureName))
				{
					SetUniqueFeatureName(i);
					continue;
				}
				int count2 = i - 1;
				for (int j = 0; j <= count2; j++)
				{
					if (GetFeatureName(j).Equals(featureName, StringComparison.CurrentCultureIgnoreCase))
					{
						SetUniqueFeatureName(i);
						break;
					}
				}
			}
		}
	}

	public void EnsureAllFeaturesHaveNames()
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (string.IsNullOrEmpty(GetFeatureName(i)))
				{
					SetUniqueFeatureName(i);
				}
			}
		}
	}

	public virtual string GetUniqueFeatureName()
	{
		return GetUniqueFeatureName(SimpleFeatureTypeName + " ");
	}

	public string GetUniqueFeatureName(string prefix)
	{
		int ordinal = 1;
		while (ContainsFeatureName(prefix + ordinal))
		{
			ordinal = checked(ordinal + 1);
		}
		return prefix + ordinal;
	}

	public bool ContainsFeatureName(string name)
	{
		return GetFeatureByName(name) != -1;
	}

	public int GetFeatureByName(string name)
	{
		if (name == null)
		{
			return -1;
		}
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				if (string.IsNullOrEmpty(GetFeatureName(i)))
				{
					if (string.IsNullOrEmpty(name))
					{
						return i;
					}
				}
				else if (Operators.CompareString(GetFeatureName(i).Trim().ToLower(), name.Trim().ToLower(), TextCompare: false) == 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	public string GetUniqueColumnStringValue(string columnName, string prefix)
	{
		DataTable dt = FeatureTable();
		int indexOf = FeatureTable().Columns.IndexOf(columnName);
		if (indexOf == -1)
		{
			return "";
		}
		int ordinal = 1;
		while (ContainsColumnStringValue(dt, indexOf, prefix + " " + ordinal))
		{
			ordinal = checked(ordinal + 1);
		}
		return prefix + " " + ordinal;
	}

	public bool ContainsColumnStringValue(int colIdx, string stringValue)
	{
		return ColumnStringValueIndex(FeatureTable(), colIdx, stringValue) >= 0;
	}

	public bool ContainsColumnStringValue(string colName, string stringValue)
	{
		return ColumnStringValueIndex(FeatureTable(), FeatureTable().Columns.IndexOf(colName), stringValue) >= 0;
	}

	private bool ContainsColumnStringValue(DataTable dt, int colIdx, string textValue)
	{
		return ColumnStringValueIndex(dt, colIdx, textValue) >= 0;
	}

	private int ColumnStringValueIndex(DataTable dt, int colIdx, string textValue)
	{
		checked
		{
			int count = FeatureCount() - 1;
			for (int i = 0; i <= count; i++)
			{
				string row = dt.Rows[i][colIdx] as string;
				if (string.IsNullOrEmpty(row))
				{
					if (string.IsNullOrEmpty(textValue))
					{
						return i;
					}
				}
				else if (Operators.CompareString(row, textValue, TextCompare: false) == 0)
				{
					return i;
				}
			}
			return -1;
		}
	}

	protected List<TimeSeriesVariable> ReadVariableUnitDataset(string h5Filename, string dset)
	{
		return TimeSeriesVariable.Parse(h5Filename, dset);
	}

	protected List<TimeSeriesVariable> ReadVariableUnitDataset(H5Reader h5r, string dset)
	{
		return TimeSeriesVariable.Parse(h5r, dset);
	}

	public virtual bool FeatureIsHitTestable(int fid)
	{
		return true;
	}

	public virtual void CopySelectedFeaturesToSharedDataClipboard()
	{
		SharedData.CopiedFeatures.Clear();
		SharedData.CopiedFeaturesDataRows.Clear();
		SharedData.CopiedFromDataRows.Clear();
		SharedData.CopiedFeatureLayer = this;
		List<Feature> features = new List<Feature>();
		List<int> fiDs = SelectedFeatures.FIDs().ToList();
		foreach (int fiDIndex in fiDs)
		{
			features.Add(Feature(fiDIndex));
		}
		CopyFeaturesToSharedDataClipboardInternal(features, this, fiDs);
	}

	private static void CopyFeaturesToSharedDataClipboardInternal(IList<Feature> features, FeatureLayer featLayer = null, IList<int> fids = null)
	{
		bool condition = false;
		checked
		{
			int count = features.Count - 1;
			for (int i = 0; i <= count; i++)
			{
				Feature feature = features[i];
				if (feature == null)
				{
					condition = true;
					continue;
				}
				Feature feature2 = feature.EnsureImmutable();
				SharedData.CopiedFeatures.Add(feature2);
				if (featLayer != null)
				{
					DataRow dataRow = featLayer.FeatureTable().NewRow();
					dataRow.ItemArray = (object[])featLayer.FeatureTable().Rows[fids[i]].ItemArray.Clone();
					dataRow[featLayer.FeatureColumnName] = feature2;
					SharedData.CopiedFeaturesDataRows.Add(dataRow);
					SharedData.CopiedFromDataRows.Add(new WeakReference<DataRow>(featLayer.FeatureTable().Rows[fids[i]]));
				}
			}
			if (condition)
			{
				SharedData.ReportMessage("Some features did not have GIS coordinates so they were not copied");
			}
		}
	}

	public static void CopyFeatureToSharedDataClipboard(IList<Feature> feats)
	{
		SharedData.CopiedFeatures.Clear();
		SharedData.CopiedFeaturesDataRows.Clear();
		SharedData.CopiedFromDataRows.Clear();
		SharedData.CopiedFeatureLayer = null;
		CopyFeaturesToSharedDataClipboardInternal(feats);
	}

	public void CopyAllFeaturesToSharedDataClipboard()
	{
		SharedData.CopiedFeatures.Clear();
		SharedData.CopiedFeaturesDataRows.Clear();
		SharedData.CopiedFromDataRows.Clear();
		SharedData.CopiedFeatureLayer = this;
		CopyFeaturesToSharedDataClipboardInternal(Features().ToList(), this, Enumerable.Range(0, FeatureCount()).ToList());
	}

	protected internal virtual string GetCMSTitle(int fid)
	{
		string textValue = "";
		if (this is IGeometryLayer && !((IGeometryLayer)this).Geometry.IsResultFile)
		{
			textValue = ((IGeometryLayer)this).Geometry.Name.InParentheses();
		}
		else if (this is IGeometryLayer && ((IGeometryLayer)this).Geometry.IsResultFile)
		{
			textValue = ((IGeometryLayer)this).Geometry.Results.PlanAttributes.PlanShortID.InParentheses();
			if (Operators.CompareString(((IGeometryLayer)this).Geometry.GeometryShortID(), ((IGeometryLayer)this).Geometry.Results.PlanAttributes.PlanShortID, TextCompare: false) == 0)
			{
				textValue += " (Results)";
			}
		}
		return SimpleFeatureTypeName + ": " + GetFeatureDisplayLabel(fid) + " " + textValue;
	}

	protected virtual TerrainLayer GetAssociatedTerrainLayer()
	{
		if (this is IGeometryLayer)
		{
			return GeometryLayerExtensions.GetAssociatedTerrainLayer((IGeometryLayer)this);
		}
		return null;
	}

	public void AddSelected(int featureID)
	{
		SelectedFeatures.Add(featureID);
	}

	public void ClearSelection()
	{
		SelectedFeatures.ClearSelection();
	}

	public SpatialIndex<int> EnsureSpatialIndex()
	{
		if (_spatialIndex == null)
		{
			_spatialIndex = SpatialIndex.GetAppropriateSpatialIndex(this);
		}
		return _spatialIndex;
	}

	public override bool SetSelection(PointM pointM, double nearDistance, SelectionMode selectionMode, ref bool layerNeedsRedraw)
	{
		return SetSelection(new Extent(pointM, nearDistance, nearDistance), selectionMode, SelectionRange.SelectSingle, ref layerNeedsRedraw);
	}

	public override bool SetSelection(Extent ext, SelectionMode selectionMode, SelectionRange selectionRange, ref bool layerNeedsRedraw)
	{
		layerNeedsRedraw = false;
		Stopwatch stopwatch = Stopwatch.StartNew();
		SelectedFeature firstOpenFeature = SelectedFeatures.GetFirstOpenFeature();
		checked
		{
			if (firstOpenFeature != null)
			{
				if (!(Feature(firstOpenFeature.FID) is MultiPoint multiPoint))
				{
					return false;
				}
				multiPoint.SelectPoints(ext, selectionMode, selectionRange, firstOpenFeature);
			}
			else
			{
				switch (selectionMode)
				{
				case SelectionMode.SelectNew:
				{
					SelectedFeatures.ClearSelection();
					List<int> intList = new List<int>();
					int count = FeatureCount() - 1;
					for (int l = 0; l <= count; l++)
					{
						Feature feature2 = Feature(l);
						if (feature2 != null && feature2.Count != 0 && (FeatureRowFilter == null || FeatureRowFilter(FeatureRow(l))) && feature2.Intersects(ext) && FeatureIsHitTestable(l))
						{
							intList.Add(l);
							if (selectionRange == SelectionRange.SelectSingle)
							{
								break;
							}
						}
					}
					SelectedFeatures.AddRange(intList);
					break;
				}
				case SelectionMode.Append:
				{
					int count2 = FeatureCount() - 1;
					for (int j = 0; j <= count2; j++)
					{
						if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(j))) && Feature(j).Intersects(ext) && FeatureIsHitTestable(j))
						{
							SelectedFeatures.Add(j);
						}
					}
					break;
				}
				case SelectionMode.Deselect:
				{
					int count3 = FeatureCount() - 1;
					for (int k = 0; k <= count3; k++)
					{
						if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(k))) && Feature(k).Intersects(ext) && FeatureIsHitTestable(k))
						{
							SelectedFeatures.Remove(k);
						}
					}
					break;
				}
				case SelectionMode.Toggle:
				{
					int count4 = FeatureCount() - 1;
					for (int i = 0; i <= count4; i++)
					{
						Feature feature = Feature(i);
						if (feature != null && feature.Count != 0 && (FeatureRowFilter == null || FeatureRowFilter(FeatureRow(i))) && feature.Intersects(ext) && FeatureIsHitTestable(i))
						{
							if (SelectedFeatures.Contains(i))
							{
								SelectedFeatures.Remove(i);
							}
							else
							{
								SelectedFeatures.Add(i);
							}
						}
					}
					break;
				}
				default:
					throw new NotImplementedException();
				}
			}
			stopwatch.Stop();
			_ = SelectedFeatures.Count;
			return true;
		}
	}

	public virtual List<int> SpatialQueryFIDs(PointM pt, double dist)
	{
		if (FeatureCount() == 0)
		{
			return new List<int>();
		}
		return (from x in (from idx in EnsureSpatialIndex().GetElements(pt, dist)
				orderby idx
				select idx).ToList().Distinct()
			where (FeatureRowFilter != null) ? (FeatureRowFilter(FeatureRow(x)) && Feature(x).Intersects(pt, dist) && FeatureIsHitTestable(x)) : (Feature(x).Intersects(pt, dist) && FeatureIsHitTestable(x))
			select x).ToList();
	}

	public virtual List<int> SpatialQueryFIDs(Extent ext)
	{
		if (FeatureCount() == 0)
		{
			return new List<int>();
		}
		return (from x in (from idx in EnsureSpatialIndex().GetElements(ext)
				orderby idx
				select idx).ToList().Distinct()
			where (FeatureRowFilter != null) ? (FeatureRowFilter(FeatureRow(x)) && Feature(x).Intersects(ext) && FeatureIsHitTestable(x)) : (Feature(x).Intersects(ext) && FeatureIsHitTestable(x))
			select x).ToList();
	}

	public virtual int SpatialQueryFID(PointM pt, double dist)
	{
		if (FeatureCount() == 0)
		{
			return -1;
		}
		List<int> intList = (from idx in EnsureSpatialIndex().GetElements(pt, dist)
			orderby idx
			select idx).ToList();
		checked
		{
			if (intList.Count > 0)
			{
				for (int countOffset = intList.Count - 1; countOffset >= 0; countOffset += -1)
				{
					int ordinal = intList[countOffset];
					if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(ordinal))) && Feature(ordinal).Intersects(pt, dist) && FeatureIsHitTestable(ordinal))
					{
						return ordinal;
					}
				}
				return -1;
			}
			return -1;
		}
	}

	public virtual int SpatialQueryFID(Extent ext)
	{
		if (FeatureCount() == 0)
		{
			return -1;
		}
		List<int> intList = (from idx in EnsureSpatialIndex().GetElements(ext)
			orderby idx
			select idx).ToList();
		checked
		{
			if (intList.Count > 0)
			{
				for (int countOffset = intList.Count - 1; countOffset >= 0; countOffset += -1)
				{
					int ordinal = intList[countOffset];
					if ((FeatureRowFilter == null || FeatureRowFilter(FeatureRow(ordinal))) && Feature(ordinal).Intersects(ext) && FeatureIsHitTestable(ordinal))
					{
						return ordinal;
					}
				}
				return -1;
			}
			return -1;
		}
	}

	public List<int> SpatialQuerySubfeatures(int fid, Extent ext)
	{
		List<int> intList = new List<int>();
		Feature feature = Feature(fid);
		if (FeatureRowFilter != null && !FeatureRowFilter(FeatureRow(fid)))
		{
			return intList;
		}
		if (!(feature is MultiPoint multiPoint))
		{
			return intList;
		}
		checked
		{
			for (int i = multiPoint.Count - 1; i >= 0; i += -1)
			{
				Feature feature2 = Feature(fid);
				if (feature2 != null && feature2.Intersects(ext) && FeatureIsHitTestable(fid))
				{
					intList.Add(i);
				}
			}
			return intList;
		}
	}

	public List<int> SpatialQuerySubfeatures(int fid, PointM pt, double dist)
	{
		List<int> intList = new List<int>();
		Feature feature = Feature(fid);
		if (FeatureRowFilter != null && !FeatureRowFilter(FeatureRow(fid)))
		{
			return intList;
		}
		if (!FeatureIsHitTestable(fid))
		{
			return intList;
		}
		if (!(feature is MultiPoint multiPoint))
		{
			return intList;
		}
		List<int> intListList = null;
		if (multiPoint.UsingQuadTree)
		{
			QuadTree<int> pointQuadTree = multiPoint.GetPointQuadTree();
			if (pointQuadTree == null)
			{
				return intList;
			}
			intListList = pointQuadTree.GetAllValuesWithinCircle(pt.X, pt.Y, dist);
		}
		else
		{
			SpatialIndex<int> pointSpatialIndex = multiPoint.GetPointSpatialIndex();
			if (pointSpatialIndex == null)
			{
				return intList;
			}
			intListList = pointSpatialIndex.GetElements(pt.X, pt.Y, dist);
		}
		foreach (int ordinal in intListList)
		{
			if (multiPoint.PointM(ordinal).CloseTo(pt, dist))
			{
				intList.Add(ordinal);
			}
		}
		return intList;
	}

	public int SpatialQuerySubfeature(int fid, Extent ext)
	{
		throw new NotImplementedException();
	}

	public virtual int SpatialQuerySubfeature(int fid, PointM pt, double dist)
	{
		Feature feature = Feature(fid);
		if (FeatureRowFilter != null && !FeatureRowFilter(FeatureRow(fid)))
		{
			return -1;
		}
		if (!(feature is MultiPoint multiPoint))
		{
			return -1;
		}
		if (multiPoint.Count == 0)
		{
			return -1;
		}
		List<int> intList = null;
		if (multiPoint.UsingQuadTree)
		{
			QuadTree<int> pointQuadTree = multiPoint.GetPointQuadTree();
			if (pointQuadTree == null)
			{
				return -1;
			}
			intList = pointQuadTree.GetAllValuesWithinCircle(pt.X, pt.Y, dist);
		}
		else
		{
			SpatialIndex<int> pointSpatialIndex = multiPoint.GetPointSpatialIndex();
			if (pointSpatialIndex == null)
			{
				return -1;
			}
			intList = pointSpatialIndex.GetElements(pt.X, pt.Y, dist);
		}
		foreach (int ordinal in intList)
		{
			if (multiPoint.PointM(ordinal).CloseTo(pt, dist))
			{
				return ordinal;
			}
		}
		return -1;
	}

	public Extent GetSelectionExtent()
	{
		if (SelectedFeatures == null)
		{
			return null;
		}
		if (SelectedFeatures.Count == 0)
		{
			return null;
		}
		Extent extent = null;
		foreach (SelectedFeature selectedFeature in SelectedFeatures)
		{
			Feature feature = Feature(selectedFeature.FID);
			if (feature != null)
			{
				if (FeatureRowFilter != null && !FeatureRowFilter(FeatureRow(selectedFeature.FID)))
				{
					throw new Exception("You filtered out a selected feature, it should not have been selected in the first place.");
				}
				Extent extent2 = feature.GetExtent();
				if (!Extent.IsNullOrInfinite(extent2))
				{
					extent = ((extent != null) ? extent2.UnionXY(extent) : extent2);
					_ = selectedFeature.IsOpen;
				}
			}
		}
		return extent;
	}

	public PointM ComputeCentroidSelectedFeatures()
	{
		double computed = 0.0;
		double computed2 = 0.0;
		foreach (int fiDIndex in SelectedFeatures.FIDs())
		{
			Feature feature = Feature(fiDIndex);
			if (FeatureRowFilter != null && !FeatureRowFilter(FeatureRow(fiDIndex)))
			{
				throw new Exception("You filtered out a selected feature, it should not have been selected in the first place.");
			}
			PointM pointM = feature.Centroid();
			computed += pointM.X;
			computed2 += pointM.Y;
		}
		double ax = computed / (double)SelectedFeatures.Count;
		double ay = computed2 / (double)SelectedFeatures.Count;
		return new PointM(ax, ay);
	}

	protected override void XMLSaveMetadata(XmlDocument doc, XmlElement xmlLayer)
	{
		base.XMLSaveMetadata(doc, xmlLayer);
		List<DataColumnGroup> uniqueColumnGroups = ((!(this is IVirtualFeatureLayer)) ? GetUniqueColumnGroups() : GetUniqueColumnGroupCopies());
		XmlElement xmlElement = doc.CreateElement("DataColumnGroupVisibilities");
		bool condition = false;
		foreach (DataColumnGroup uniqueColumnGroup in uniqueColumnGroups)
		{
			if (_columnGroupDefaultVisibilities.ContainsKey(uniqueColumnGroup.DisplayName) && _columnGroupDefaultVisibilities[uniqueColumnGroup.DisplayName] != uniqueColumnGroup.IsVisible)
			{
				XmlElement xmlElement2 = doc.CreateElement("DataColumnGroupVisibility");
				xmlElement.AppendChild(xmlElement2);
				xmlElement2.SetAttribute("DisplayName", uniqueColumnGroup.DisplayName);
				xmlElement2.SetAttribute("IsVisible", uniqueColumnGroup.IsVisible.ToString());
				condition = true;
			}
		}
		if (condition)
		{
			xmlLayer.AppendChild(xmlElement);
		}
		if (!(this is IRasmapFeatureLayer))
		{
			return;
		}
		IRasmapFeatureLayer rasmapFeatureLayer = (IRasmapFeatureLayer)this;
		XmlElement xmlElement3 = doc.CreateElement(rasmapFeatureLayer.XMLGroupName);
		xmlLayer.AppendChild(xmlElement3);
		checked
		{
			if (FeatureCount() != 0)
			{
				int count = FeatureCount() - 1;
				for (int i = 0; i <= count; i++)
				{
					string featureName = GetFeatureName(i);
					XmlElement xmlElement4 = doc.CreateElement(rasmapFeatureLayer.XMLElementName);
					xmlElement3.AppendChild(xmlElement4);
					xmlElement4.SetAttribute("Name", featureName);
					xmlElement4.SetAttribute("Type", "XY");
					xmlElement4.InnerText = FeatureToCSV(i);
				}
			}
		}
	}

	protected override void XMLLoadMetadata(XmlElement xmlLayer)
	{
		base.XMLLoadMetadata(xmlLayer);
		XmlNodeList xmlNodeList = xmlLayer.SelectNodes("DataColumnGroupVisibilities/DataColumnGroupVisibility");
		List<DataColumnGroup> uniqueColumnGroups = ((!(this is IVirtualFeatureLayer)) ? GetUniqueColumnGroups() : GetUniqueColumnGroupCopies());
		foreach (XmlNode xmlNodeList2 in xmlNodeList)
		{
			string right = XML.StringAttribute(xmlNodeList2, "DisplayName");
			bool isVisible = XML.BooleanAttribute(xmlNodeList2, "IsVisible");
			foreach (DataColumnGroup uniqueColumnGroup in uniqueColumnGroups)
			{
				if (Operators.CompareString(uniqueColumnGroup.DisplayName, right, TextCompare: false) == 0)
				{
					uniqueColumnGroup.IsVisible = isVisible;
					break;
				}
			}
		}
		if (!(this is IRasmapFeatureLayer))
		{
			return;
		}
		IRasmapFeatureLayer rasmapFeatureLayer = (IRasmapFeatureLayer)this;
		XmlNode xmlNode2 = xmlLayer.SelectSingleNode(rasmapFeatureLayer.XMLGroupName);
		FeatureTable();
		if (xmlNode2 == null)
		{
			return;
		}
		foreach (XmlNode selectNode in xmlNode2.SelectNodes(rasmapFeatureLayer.XMLElementName))
		{
			try
			{
				string attributesSelectNodeTerm = selectNode.Attributes["Name"].Value;
				string attributesSelectNodeTerm2 = selectNode.Attributes["Type"].Value;
				string innerText = selectNode.InnerText;
				Feature feature = CSVToFeature(innerText, attributesSelectNodeTerm2);
				if (feature != null)
				{
					AddFeature(feature)["Name"] = attributesSelectNodeTerm;
				}
			}
			catch (Exception ex)
			{
				ProjectData.SetProjectError(ex);
				Exception ex2 = ex;
				ProjectData.ClearProjectError();
			}
		}
	}

	public override List<string> GetAllSourceFiles()
	{
		if (ShapefileStorage.IsValidFilename(SourceFilename))
		{
			return ShapefileStorage.GetAllFiles(SourceFilename);
		}
		return base.GetAllSourceFiles();
	}

	protected virtual string FeatureToCSV(int fId)
	{
		return "";
	}

	protected virtual Feature CSVToFeature(string featureCSV, string typeName)
	{
		return null;
	}
}
