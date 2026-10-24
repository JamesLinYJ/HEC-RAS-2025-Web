using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility.DataStructures;
using Utility.Files;
using Utility.Logging;
using Utility.Units;

namespace Ras.Observables;

public class ObservableGeometry : ObservableCoreLayer
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableGeometry");

	private ObservableNValueLayer _associatedNValueLayer;

	private int _lastBackupHash = -1;

	public static Type Type => typeof(ObservableGeometry);

	public new Geometry Layer => base.Layer as Geometry;

	public Geometry Geometry => Layer;

	public ObservableConceptualMeshLayer ConceptualMeshLayer { get; }

	public ObservableFlowAreaLayer FlowAreaLayer { get; }

	public ObservableCulvertBarrelLayer CulvertBarrelLayer { get; }

	public ObservableBarrelPropertiesLayer BarrelPropertiesLayer { get; }

	public ObservableOpeningPropertiesLayer OpeningPropertiesLayer { get; }

	public ObservableTerrain AssociatedTerrain
	{
		get
		{
			Terrain actualAssn = Geometry.AssociatedTerrain;
			if (actualAssn == null)
			{
				return null;
			}
			ObservableTerrain observableTerrain = Project.Terrains.FirstOrDefault((ObservableTerrain obsTerr) => obsTerr.Terrain.Equals(actualAssn));
			if (observableTerrain == null)
			{
				_logger.LogWarning("Could not access ObservableGeometry.AssociatedTerrain - Terrain doesn't have a valid observable bound?");
				return null;
			}
			return observableTerrain;
		}
		set
		{
			GeometryAssociation geometryAssociation = Project.Project.GetGeometryAssociation(Geometry);
			if (geometryAssociation != null && geometryAssociation.Terrain != (value?.Terrain ?? null))
			{
				geometryAssociation.Terrain = value?.Terrain ?? null;
				OnPropertyChanged("AssociatedTerrain");
			}
		}
	}

	public ObservableNValueLayer AssociatedNValueLayer
	{
		get
		{
			if (_associatedNValueLayer == null && Geometry.AssociatedNValueLayer != null)
			{
				_associatedNValueLayer = Project.NValues.FirstOrDefault((ObservableNValueLayer obsNVal) => obsNVal.Layer.Equals(Geometry.AssociatedNValueLayer));
			}
			return _associatedNValueLayer;
		}
		set
		{
			GeometryAssociation geometryAssociation = Project.Project.GetGeometryAssociation(Geometry);
			if (geometryAssociation == null)
			{
				if (!Geometry.IsResult)
				{
					throw new InvalidOperationException("Somehow a geometry's association is null, but it's not a result.");
				}
			}
			else if (geometryAssociation.NValueLayer != (value?.Layer ?? null))
			{
				Project.Project.GetGeometryAssociation(Geometry).NValueLayer = value?.Layer ?? null;
				SetProperty(ref _associatedNValueLayer, value, "AssociatedNValueLayer");
			}
		}
	}

	public AdvancedCollection<string> BackupFiles { get; init; }

	public bool CanMutate
	{
		get
		{
			if (IsResult)
			{
				return false;
			}
			if (FlowAreaLayer.IsComputingPropertyTables)
			{
				return false;
			}
			return true;
		}
	}

	public bool IsResult => Layer.IsResult;

	public ObservableResult Result { get; }

	public event EventHandler CanMutateChanged;

	internal void NotifyCanMutateChanged()
	{
		OnPropertyChanged("CanMutate");
		CanMutateChanged?.Invoke(this, EventArgs.Empty);
	}

	public ObservableGeometry(ObservableProject prj, Geometry geom)
		: base(prj, geom)
	{
		ConceptualMeshLayer = new ObservableConceptualMeshLayer(this);
		FlowAreaLayer = new ObservableFlowAreaLayer(this);
		FlowAreaLayer.PropertyChanged += (object? ob, PropertyChangedEventArgs ev) =>
		{
			if (ev.PropertyName == "IsComputingPropertyTables")
			{
				OnPropertyChanged("CanMutate");
			}
		};
		CulvertBarrelLayer = new ObservableCulvertBarrelLayer(this);
		BarrelPropertiesLayer = new ObservableBarrelPropertiesLayer(this);
		OpeningPropertiesLayer = new ObservableOpeningPropertiesLayer(this);
		_lastBackupHash = Geometry.GetHashVersionForBackup();
		AdvancedCollection<string> advancedCollection = new AdvancedCollection<string>();
		foreach (string backupFilesFromDisk in GetBackupFilesFromDisk())
		{
			advancedCollection.Add(backupFilesFromDisk);
		}
		BackupFiles = advancedCollection;
	}

	public ObservableGeometry(ObservableResult res)
		: base(res.Project, res.Result.Geometry)
	{
		Result = res;
		ConceptualMeshLayer = new ObservableConceptualMeshLayer(this);
		FlowAreaLayer = new ObservableFlowAreaLayer(this);
		FlowAreaLayer.PropertyChanged += (object? ob, PropertyChangedEventArgs ev) =>
		{
			if (ev.PropertyName == "IsComputingPropertyTables")
			{
				OnPropertyChanged("CanMutate");
			}
		};
		CulvertBarrelLayer = new ObservableCulvertBarrelLayer(this);
		BarrelPropertiesLayer = new ObservableBarrelPropertiesLayer(this);
		OpeningPropertiesLayer = new ObservableOpeningPropertiesLayer(this);
		_lastBackupHash = Geometry.GetHashVersionForBackup();
		AdvancedCollection<string> advancedCollection = new AdvancedCollection<string>();
		foreach (string backupFilesFromDisk in GetBackupFilesFromDisk())
		{
			advancedCollection.Add(backupFilesFromDisk);
		}
		BackupFiles = advancedCollection;
	}

	public ObservableGeometry Clone(string newName, bool fixNameIfExists)
	{
		Geometry geom = Layer.Clone(newName, fixNameIfExists);
		ObservableGeometry observableGeometry = new ObservableGeometry(Project, geom);
		Project.TryAddGeometry(observableGeometry);
		return observableGeometry;
	}

	public string CheckCreateBackup(string backupName = "")
	{
		if (Geometry.IsInMemory)
		{
			return "";
		}
		if (Geometry.IsResult)
		{
			return "";
		}
		if (Geometry.IsImported.HasValue && Geometry.IsImported.Value)
		{
			return "";
		}
		int hashVersionForBackup = Geometry.GetHashVersionForBackup();
		if (string.IsNullOrEmpty(backupName) && _lastBackupHash == hashVersionForBackup)
		{
			return "";
		}
		_lastBackupHash = hashVersionForBackup;
		string backupDirectory = BackupDirectory();
		if (!Directory.Exists(backupDirectory))
		{
			Directory.CreateDirectory(backupDirectory);
		}
		string baseFilename = ((!string.IsNullOrEmpty(backupName)) ? Path.Combine(backupDirectory, backupName + ".h5") : Path.Combine(backupDirectory, "Backup " + Time.CurrentDateFilenameSafe() + ".h5"));
		baseFilename = FileHelpers.UniqueFilename(baseFilename);
		Geometry.SaveGeometryBackup(baseFilename);
		BackupFiles.Add(baseFilename);
		return baseFilename;
	}

	public string BackupDirectory()
	{
		return Path.Combine(Path.GetDirectoryName(Filename), Path.GetFileNameWithoutExtension(Filename) + " Backups");
	}

	public List<string> GetBackupFilesFromDisk()
	{
		if (IsInMemory)
		{
			return new List<string>();
		}
		string path = BackupDirectory();
		if (!Directory.Exists(path))
		{
			return new List<string>();
		}
		return Directory.GetFiles(path).ToList();
	}

	public override bool TryRenameByDeleteAndSaveAsH5(string inputName, out string errorText)
	{
		if (!TryRenameByDeleteAndSaveAsH5Internal(inputName, Geometry.MakeFilename, out errorText))
		{
			return false;
		}
		foreach (ObservablePlan plan in Project.Plans)
		{
			if (plan.AssociatedGeometry == this)
			{
				plan.Plan.PlanAssociation.AlertGeometryRenamed(inputName);
			}
		}
		return true;
	}

	public static ValidationResult ValidateName(string n, ValidationContext context)
	{
		if (!(context.ObjectInstance is ObservableGeometry observableGeometry))
		{
			return new ValidationResult("Unknown validation error - null instance.");
		}
		if (!observableGeometry.Layer.CanSetName)
		{
			return new ValidationResult("Geometry name cannot be modified.");
		}
		if (!CoreLayerBase.IsLegalName(n, out var errorMessage))
		{
			return new ValidationResult(errorMessage);
		}
		return ValidationResult.Success;
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemoveGeometry(this);
		Geometry.DisposeAndDeleteAllBackingFiles();
	}
}
