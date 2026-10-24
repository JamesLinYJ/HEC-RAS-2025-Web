using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Geospatial.GDALAssist;
using Geospatial.IO.ORM;
using H5Assist;
using H5Assist.Caching;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Files;
using Utility.Hashing;
using Utility.Interfaces;
using Utility.Logging;
using Utility.ORM;
using Utility.Units;

namespace Ras.Layers;

public abstract class CoreLayerBase : IDisposable, IDataHash, IReadOnlyDisplayName
{
	protected static readonly ILogger _logger = LogHelper.GetLogger("CoreLayerBase");

	public static readonly char[] IllegalNameCharacters = Path.GetInvalidFileNameChars();

	public const int MaxNameLength = 64;

	protected bool _isLoading;

	protected bool _isDisposed;

	protected NativeResourceWrapper<H5Reader> _h5Wrapper;

	protected string _name = string.Empty;

	private const int HashNDV = int.MinValue;

	private int _metadataLoadHash = int.MinValue;

	private readonly object _loadLock = new object();

	public string Filename { get; protected set; }

	public virtual Project Project { get; set; }

	[Metadata("Date Created")]
	public string DateCreated { get; protected set; }

	[Metadata("Date Last Modified")]
	public string DateLastModified { get; protected set; }

	[Metadata("File Version")]
	public string LoadedFileVersion { get; set; }

	[Metadata("Software Version")]
	public string LoadedSoftwareVersion { get; set; }

	[Metadata("Software")]
	public string LoadedSoftware => Project.SoftwareName;

	[Metadata("Origin Software", Optional = true)]
	public string OriginSoftware { get; set; }

	[Metadata("Origin Software Version", Optional = true)]
	public string OriginVersion { get; set; }

	public bool? IsImported
	{
		get
		{
			if (string.IsNullOrEmpty(OriginSoftware))
			{
				return null;
			}
			if (OriginSoftware == Project.SoftwareName)
			{
				return false;
			}
			if (OriginSoftware == Project.ImporterSoftwareName)
			{
				return true;
			}
			return null;
		}
	}

	public InstantiationMode InstantiationMode { get; protected set; }

	public bool IsInMemory
	{
		get
		{
			if (InstantiationMode != InstantiationMode.InMemory)
			{
				return InstantiationMode == InstantiationMode.LoadFromDataProvider;
			}
			return true;
		}
	}

	public string SourceDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return Path.GetDirectoryName(Filename);
			}
			return "";
		}
	}

	public IReadOnlyList<Message> LayerErrors { get; private set; }

	public bool HasLoadErrors => (LayerErrors?.Count ?? 0) > 0;

	[HashContributor]
	[Metadata("Name", Optional = true, SetValueToDefaultIfKeyMissing = false)]
	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (!_isLoading && !IsInMemory)
			{
				throw new NotSupportedException("Setting the name is not supported for existing files on disk. Please use SaveAs");
			}
			_name = value;
		}
	}

	string IReadOnlyDisplayName.DisplayName => Name;

	public virtual bool NameMustMatchFilename => true;

	public bool CanSetName => IsInMemory;

	public void OverrideName(string name)
	{
		_name = name;
	}

	[HashContributor]
	[Metadata("Description", Optional = true)]
	public string Description { get; set; }

	public EmbeddedImageCollection EmbeddedImages { get; private set; }

	protected string EmbeddedImageGroup => GetHDF5Group() + "/Images";

	public event EventHandler ErrorsChanged;

	public abstract string GetCurrentFileFormatVersion();

	public virtual void AlertFileRenaming(string newFilename)
	{
		_h5Wrapper?.Dispose();
		_h5Wrapper = null;
	}

	public virtual void AlertFileRenamed(string newFilename)
	{
		Filename = newFilename;
		_name = Path.GetFileNameWithoutExtension(Filename);
		if (_h5Wrapper != null)
		{
			_h5Wrapper.Dispose();
		}
		_h5Wrapper = new NativeResourceWrapper<H5Reader>(() => new H5Reader(newFilename));
	}

	protected CoreLayerBase(InstantiationMode mode)
	{
		InstantiationMode = mode;
		if (mode == InstantiationMode.InMemory || mode == InstantiationMode.CreateNewFile)
		{
			EmbeddedImages = EmbeddedImageCollection.InMemory();
			DateCreated = Time.CurrentDateFormatted();
			OriginSoftware = LoadedSoftware;
			OriginVersion = Project.AssemblySoftwareVersion;
			if (mode == InstantiationMode.InMemory)
			{
				_h5Wrapper = H5ResourceWrapper.Give(H5Writer.CreateInMemory());
			}
		}
		_metadataLoadHash = GetDataHash();
	}

	protected CoreLayerBase(string filename, Project project = null)
		: this(InstantiationMode.ReadFromDisk)
	{
		if (!File.Exists(filename))
		{
			throw new FileNotFoundException(filename);
		}
		Filename = filename;
		Project = project;
		_h5Wrapper = H5ResourceWrapper.OpenOnDemand(filename);
		string name = (_name = Path.GetFileNameWithoutExtension(Filename));
		InitializeData();
		if (NameMustMatchFilename)
		{
			_name = name;
		}
	}

	protected abstract string GetHDF5Group();

	protected virtual void PreInitializeDataInternal()
	{
	}

	protected virtual void LoadInternal(H5Reader hr)
	{
	}

	protected virtual void PostInitializeDataInternal()
	{
	}

	protected void InitializeData()
	{
		PreInitializeDataInternal();
		_h5Wrapper.UseResource((H5Reader hr) =>
		{
			_isLoading = true;
			try
			{
				hr.AssertLoadedFileID();
				Stopwatch stopwatch = Stopwatch.StartNew();
				string hDF5Group = GetHDF5Group();
				GeospatialH5Metadata.Read(this, hr, hDF5Group);
				LoadInternal(hr);
				stopwatch.Stop();
				_logger.LogDebug("{Filename} loaded {H5Group} (lightweight) in {Duration} ms.", Path.GetFileName(Filename), hDF5Group, stopwatch.Elapsed.TotalMilliseconds);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error loading {Filename}", Filename);
				AddLoadError(new MessageClass("CoreLayer_LoadError", MessageSeverity.Error, ex.Message).Create());
			}
			finally
			{
				_isLoading = false;
			}
		});
		EmbeddedImages = new EmbeddedImageCollection(EmbeddedImageGroup, new Any<NativeResourceWrapper<H5Reader>>(new Lazy<NativeResourceWrapper<H5Reader>>(() => _h5Wrapper)));
		PostInitializeDataInternal();
		_metadataLoadHash = GetDataHash();
	}

	internal List<T> CheckInitializeData<T>(ref List<T> data, Func<H5Reader, List<T>> initFromDisk)
	{
		if (data != null)
		{
			return data;
		}
		if (InstantiationMode == InstantiationMode.InMemory || InstantiationMode == InstantiationMode.CreateNewFile)
		{
			_logger.LogTrace("\"{File}\" CheckInitializeData short-circuited new List<{Type}>. InstantiationMode := {InstantiationMode}", typeof(T).Name, Filename, InstantiationMode);
			data = LazyThreadsafe<List<T>>.AllocateOnce(ref data, _loadLock, () => new List<T>());
			return data;
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		data = LazyThreadsafe<List<T>>.AllocateOnce(ref data, _loadLock, () => _h5Wrapper.UseResource((H5Reader h5r) => initFromDisk(h5r)));
		stopwatch.Stop();
		_logger.LogTrace("\"{File}\" loaded {type} table ({ct}) in {Duration} ms.", _name, typeof(T).Name, data.Count, stopwatch.ElapsedMilliseconds);
		return data;
	}

	public void UseReader(Action<H5Reader> action)
	{
		_h5Wrapper.UseResource(action);
	}

	public T UseReader<T>(Func<H5Reader, T> action)
	{
		return _h5Wrapper.UseResource(action);
	}

	public NativeResourceWrapper<H5Reader> GetH5Wrapper()
	{
		return _h5Wrapper.CopyAsLoanedResource();
	}

	public bool CanElevateToWriter()
	{
		if (IsInMemory)
		{
			H5Reader resourceIfExists = _h5Wrapper.ResourceIfExists;
			if (resourceIfExists != null)
			{
				return resourceIfExists is H5Writer;
			}
			return false;
		}
		return true;
	}

	public void UseWriter(Action<H5Writer> action)
	{
		H5Reader h5Reader = _h5Wrapper?.ResourceIfExists;
		if (h5Reader != null && h5Reader is H5Writer h5Writer)
		{
			action(h5Writer);
			return;
		}
		if (IsInMemory)
		{
			throw new NotSupportedException("Could not elevate to writer.");
		}
		H5Writer filename;
		using (filename = new H5Writer(Filename))
		{
			action(filename);
		}
	}

	public bool NeedsSave()
	{
		if (_metadataLoadHash != GetDataHash())
		{
			return true;
		}
		if (NeedsSaveInternal())
		{
			return true;
		}
		if (EmbeddedImages.NeedsSave())
		{
			return true;
		}
		return false;
	}

	protected abstract bool NeedsSaveInternal();

	protected abstract void SaveInternal(H5Writer hw);

	protected virtual void CopyDataToInternal(H5Writer hw)
	{
	}

	public void Save()
	{
		if (!CanElevateToWriter())
		{
			throw new NotSupportedException("Cannot write to the underlying HDF5 resource.");
		}
		if (_h5Wrapper != null && (_h5Wrapper.ResourceMode == NativeResourceMode.OnLoan || _h5Wrapper.ResourceMode == NativeResourceMode.KeepOpen))
		{
			_logger.LogWarning("TODO - need to figure out how to close this and re-open in the same mode after we save.");
		}
		UseWriter((H5Writer hw) =>
		{
			SaveLocal(hw);
		});
		InstantiationMode = InstantiationMode.ReadFromDisk;
		if (_h5Wrapper == null)
		{
			_h5Wrapper = H5ResourceWrapper.OpenOnDemand(Filename);
			EmbeddedImages = new EmbeddedImageCollection(EmbeddedImageGroup, _h5Wrapper);
		}
		_metadataLoadHash = GetDataHash();
	}

	private void SaveLocal(H5Writer hw)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		LoadedSoftwareVersion = Project.AssemblySoftwareVersion;
		LoadedFileVersion = GetCurrentFileFormatVersion();
		DateLastModified = Time.CurrentDateFormatted();
		string hDF5Group = GetHDF5Group();
		if (!hw.ExistsAsGroup(hDF5Group))
		{
			hw.CreateGroup(hDF5Group);
		}
		GeospatialH5Metadata.Write(this, hw, hDF5Group);
		EmbeddedImages.Save(hw);
		Project?.SaveProjectMetadataToH5Header(hw);
		SaveInternal(hw);
		stopwatch.Stop();
		_logger.LogInformation("Saved {Filename} in {Duration} ms", Path.GetFileName(Filename), stopwatch.Elapsed.TotalMilliseconds);
	}

	public void SaveAs(string filename)
	{
		VerifyCanCreateNew(filename);
		using H5Writer hw = new H5Writer(filename);
		SaveAs(hw);
	}

	public void SaveAs(H5Writer hw)
	{
		SaveLocal(hw);
		CopyDataToInternal(hw);
	}

	public static void VerifyCanCreateNew(string filename)
	{
		if (string.IsNullOrEmpty(filename))
		{
			throw new ArgumentNullException("filename");
		}
		if (File.Exists(filename))
		{
			throw new Exception(filename + " already exists.");
		}
		if (Directory.Exists(filename))
		{
			throw new Exception(filename + " exists as a directory.");
		}
	}

	protected void InitializeAndSaveAsNew(string filename)
	{
		Filename = filename;
		_name = Path.GetFileNameWithoutExtension(filename);
		ForceLoadChildLayers();
		Save();
	}

	protected virtual void ForceLoadChildLayers()
	{
	}

	protected void AddLoadError(Message msg)
	{
		if (LayerErrors == null)
		{
			List<Message> layerErrors = new List<Message> { msg };
			LayerErrors = layerErrors;
		}
		else
		{
			List<Message> layerErrorsList = new List<Message>(LayerErrors.Count + 1);
			layerErrorsList.AddRange(LayerErrors);
			layerErrorsList.Add(msg);
			LayerErrors = layerErrorsList;
		}
		ErrorsChanged?.Invoke(this, null);
	}

	protected void RemoveFirstLoadError(MessageClass msgClass)
	{
		if (LayerErrors == null || LayerErrors.Count == 0)
		{
			return;
		}
		throw new NotImplementedException();
	}

	protected void ClearLoadErrorsOf(params MessageClass[] msgClasses)
	{
		if (LayerErrors != null)
		{
			int count = LayerErrors.Count;
			LayerErrors = LayerErrors.Where((Message msg) => !msgClasses.Contains(msg.MessageType, null)).ToList().AsReadOnly();
			if (count != LayerErrors.Count)
			{
				ErrorsChanged?.Invoke(this, null);
			}
		}
	}

	protected bool ContainsLoadError(MessageClass cls)
	{
		if (LayerErrors == null)
		{
			return false;
		}
		return LayerErrors.Any((Message msg) => msg.MessageType == cls);
	}

	public void LoadProjectMetadataFromH5Header(out string projectName, out Projection projection, out UnitSystem units)
	{
		string name = null;
		Projection prj = null;
		UnitSystem us = UnitSystem.USCustomary;
		UseReader((H5Reader hr) =>
		{
			Project.LoadProjectMetadataFromH5Header(hr, out name, out prj, out us);
		});
		projectName = name;
		projection = prj;
		units = us;
	}

	public void LoadBestAvailableProjectMetadata(out string projectName, out Projection projection, out UnitSystem units)
	{
		if (Project != null)
		{
			projectName = Project.Name;
			projection = Project.Projection;
			units = Project.Units;
		}
		else
		{
			LoadProjectMetadataFromH5Header(out projectName, out projection, out units);
		}
	}

	public static bool IsLegalName(string name, out string errorMessage)
	{
		if (string.IsNullOrEmpty(name))
		{
			errorMessage = "Name cannot be blank.";
			return false;
		}
		if (name.Length > 64)
		{
			errorMessage = "Name cannot be longer than " + 64 + " characters.";
			return false;
		}
		char[] illegalNameCharacters = IllegalNameCharacters;
		foreach (char illegalNameCharacter in illegalNameCharacters)
		{
			if (name.Contains(illegalNameCharacter))
			{
				errorMessage = "Name cannot contain any of these characters: " + string.Join(", ", IllegalNameCharacters);
				return false;
			}
		}
		errorMessage = "";
		return true;
	}

	public override string ToString()
	{
		return Name;
	}

	protected static string ParseNameFromFile(string fn, string extension)
	{
		return NameHelpers.ParseNameFromFile(fn, extension);
	}

	public virtual void DisposeAndDeleteAllBackingFiles()
	{
		Dispose();
		if (!IsInMemory && File.Exists(Filename))
		{
			FileHelpers.TryDelete(Filename);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!_isDisposed)
		{
			if (disposing)
			{
				_h5Wrapper?.Dispose();
			}
			_isDisposed = true;
		}
	}

	public void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public int GetDataHash()
	{
		return Hash.GetDefaultDataHash(this);
	}

	public void DebugAddError(Message msg)
	{
		AddLoadError(msg);
	}
}
