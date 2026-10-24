using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Geospatial.GDALAssist;
using Geospatial.Vectors;
using H5Assist;
using Microsoft.Extensions.Logging;
using Ras.Layers.Cleanup;
using Ras.Layers.Terrains;
using Utility;
using Utility.Compression;
using Utility.Extensions;
using Utility.Files;
using Utility.Filtering;
using Utility.Logging;
using Utility.Progress;
using Utility.Units;

namespace Ras.Layers;

public class Project
{
	public class ProjectExportConfig
	{
		public bool ClipRasters { get; set; }

		public float? ResampleTerrainCellsize { get; set; }

		public MigrateExternalConnectionBehavior ExternalConnectionBehavior { get; set; }

		public bool SaveSourceChanges { get; set; } = true;

		public static ProjectExportConfig Default()
		{
			return new ProjectExportConfig
			{
				ClipRasters = true,
				ResampleTerrainCellsize = null
			};
		}
	}

	public enum MigrateExternalConnectionBehavior
	{
		ReferenceByAbsolutePath,
		CopyToProject
	}

	public static class Keys
	{
		public const string Project = "Project";

		public const string Units = "Units";

		public const string Description = "Description";

		public const string Projection = "Projection";

		public const string PlanAssociationGroup = "PlanAssociations";

		public const string PlanAssociation = "PlanAssociation";

		public const string PlanAssociation_Plan = "Plan";

		public const string PlanAssociation_Geom = "Geometry";

		public const string PlanAssociation_BC = "BoundaryCondition";

		public const string GeometryAssociationGroup = "GeometryAssociations";

		public const string GeometryAssociation = "GeometryAssociation";

		public const string GeometryAssociation_Geom = "Geometry";

		public const string GeometryAssociation_Terrain = "Terrain";

		public const string GeometryAssociation_NValues = "N_Values";

		public const string ResultAssociationGroup = "ResultAssociations";

		public const string ResultAssociation = "ResultAssociation";

		public const string ResultAssociation_Result = "Result";

		public const string ResultAssociation_OverrideTerrain = "OverrideTerrain";

		public const string DefaultTerrain = "DefaultTerrain";

		public const string ConnectionGroup = "Connections";

		public const string CleanAuxiliary = "CleanAuxiliary";
	}

	public static class MessageCodes
	{
		public static readonly MessageClass NoProjectProjection = new MessageClass("Project_NoProjection", MessageSeverity.Warning, "Project {ProjectName} does not have a projection.");

		public static readonly MessageClass NoPlanAssociation = new MessageClass("Project_NoPlanAssociation", MessageSeverity.Error, "Project {ProjectName} does not have a plan association for {PlanName}.");

		public static Message GetNoProjectProjection(string projName)
		{
			return NoProjectProjection.Create(projName);
		}

		public static Message GetNoPlanAssociation(Project proj, Plan plan)
		{
			return NoPlanAssociation.Create(proj.Name, plan.Name);
		}

		public static Message GetNoPlanAssociation(string projName, string planName)
		{
			return NoPlanAssociation.Create(projName, planName);
		}
	}

	private static readonly ILogger _logger = LogHelper.GetLogger("Project");

	public static readonly string SoftwareName = "HEC-RAS";

	public static readonly string AssemblySoftwareVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "null";

	public static readonly string FileFormatVersion = DevFileVersion;

	public const string Extension = ".ras";

	public const string ExtensionSearchPattern = "*.ras";

	public static readonly string ImporterSoftwareName = "HEC-RAS Importer";

	public static readonly string DevFileVersion = "1.0-dev";

	public const UnitSystem DefaultUnitSystem = UnitSystem.USCustomary;

	private Dictionary<Plan, PlanAssociation> _planAssociations;

	private Dictionary<Geometry, GeometryAssociation> _geomAssociations;

	private Dictionary<Result, ResultAssociation> _resultAssociations;

	private List<Plan> _plans;

	private List<Geometry> _geometries;

	private List<BoundaryCondition> _boundaryConditions;

	private List<Terrain> _terrains;

	private List<Result> _results;

	private List<ClassificationLayer> _classificationLayers;

	private List<NValueLayer> _nValueLayers;

	private ExternalConnectionCollection _connections;

	private HydraulicDesignData _hydraulicDesign;

	// Request-scoped readers may exclude exact source files that another
	// retained workflow currently owns for writing. The default public
	// constructor keeps the original project-loading behavior.
	private Func<string, bool> _includeSourceFile = null;

	private string _filename;

	private string _name;

	private ProjectionDefinition _projectionDef;

	private Terrain _defaultTerrain;

	private NValueLayer _defaultNValueLayer;

	public static string SoftwareVersionStripAllTags
	{
		get
		{
			string assemblySoftwareVersion = AssemblySoftwareVersion;
			int indexOf = assemblySoftwareVersion.IndexOf('-');
			int indexOf2 = assemblySoftwareVersion.IndexOf('+');
			int unity = -1;
			if (indexOf == -1)
			{
				unity = indexOf2;
			}
			else
			{
				unity = ((indexOf2 != -1) ? Math.Min(indexOf2, indexOf) : indexOf);
			}
			if (unity != -1)
			{
				return assemblySoftwareVersion.Substring(0, unity);
			}
			return assemblySoftwareVersion;
		}
	}

	public static string SoftwareVersionStripCommitHash
	{
		get
		{
			string assemblySoftwareVersion = AssemblySoftwareVersion;
			int indexOf = assemblySoftwareVersion.IndexOf('+');
			if (indexOf != -1)
			{
				return assemblySoftwareVersion.Substring(0, indexOf);
			}
			return assemblySoftwareVersion;
		}
	}

	public static string SoftwareVersion => SoftwareVersionStripCommitHash;

	public string Filename
	{
		get
		{
			return _filename;
		}
		private set
		{
			_filename = value;
			if (!string.IsNullOrEmpty(_filename))
			{
				_name = Path.GetFileNameWithoutExtension(_filename);
			}
			if (_connections != null)
			{
				_connections.SetPathRelativeBase(string.IsNullOrEmpty(_filename) ? "" : Path.GetDirectoryName(Path.GetFullPath(_filename)));
			}
		}
	}

	public bool IsInMemory => string.IsNullOrEmpty(Filename);

	public string ProjectDirectory
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

	public string GeometriesDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultGeometryDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string PlansDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultPlanDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string ResultsDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultResultDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string BoundaryConditionDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultBoundaryConditionDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string TerrainsDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultTerrainsDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string SurfaceLayersDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultSurfaceLayersDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string ClassificationLayersDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultSurfaceLayersDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string NValueLayersDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultSurfaceLayersDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string HydraulicDesignDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return GetDefaultHydraulicDesignDirectory(ProjectDirectory);
			}
			return "";
		}
	}

	public string DefaultExportDirectory
	{
		get
		{
			if (!IsInMemory)
			{
				return Path.Combine(ProjectDirectory, "Exports");
			}
			return RasSpecialDirectories.GisModeExportDirectory;
		}
	}

	public string DefaultDownloadsDirectory => Path.Combine(ProjectDirectory, "Downloads");

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			if (!CanSetName)
			{
				throw new NotSupportedException("Can only set the project name if it's in-memory.");
			}
			_name = value;
		}
	}

	public bool CanSetName => IsInMemory;

	public UnitSystem Units { get; private set; }

	public DefaultUnits DefaultUnitValues => DefaultUnits.FromDefault(Units);

	public Projection Projection { get; private set; }

	public ProjectionDefinition ProjectionDefinition
	{
		get
		{
			return _projectionDef;
		}
		set
		{
			if (value == null)
			{
				throw new ArgumentNullException("ProjectionDefinition");
			}
			_projectionDef = value;
			ProjectionDefinitionSet();
		}
	}

	public IReadOnlyList<Geometry> Geometries => _geometries;

	public IReadOnlyList<Plan> Plans => _plans;

	public IReadOnlyList<Result> Results => _results;

	public IReadOnlyList<Terrain> Terrains => _terrains;

	public IReadOnlyList<NValueLayer> NValues => _nValueLayers;

	public IReadOnlyList<ClassificationLayer> ClassificationLayers => _classificationLayers;

	public IReadOnlyList<BoundaryCondition> BoundaryConditions => _boundaryConditions;

	public ExternalConnectionCollection ConnectionSource => _connections;

	public HydraulicDesignData HydraulicDesign => _hydraulicDesign;

	public string Description { get; set; }

	public CleanupPolicy CleanupPolicy { get; set; }

	public bool CleanResultAuxiliaryFiles { get; set; } = CleanupPolicyDefaults.Results.CleanAuxiliaryFiles;

	public Terrain DefaultTerrain
	{
		get
		{
			return _defaultTerrain ?? Terrains.FirstOrDefault();
		}
		set
		{
			if (value != null)
			{
				_defaultTerrain = value;
			}
		}
	}

	public NValueLayer DefaultNValueLayer
	{
		get
		{
			return _defaultNValueLayer ?? NValues.FirstOrDefault();
		}
		set
		{
			if (value != null)
			{
				_defaultNValueLayer = value;
			}
		}
	}

	public void DebugSetProjection(Projection projection)
	{
		if (!IsInMemory)
		{
			throw new InvalidOperationException("Only allowed to set in memory projects");
		}
		Projection = projection;
	}

	private void ProjectionDefinitionSet()
	{
		if (ProjectionDefinition == null)
		{
			Projection = null;
			Units = UnitSystem.USCustomary;
			return;
		}
		OperationResult operationResult = ProjectionDefinition.Evaluate(out var proj, out var units);
		if ((bool)operationResult)
		{
			Projection = proj;
			Units = units;
			return;
		}
		Projection = null;
		Units = UnitSystem.USCustomary;
		_logger.LogWarning("Bad projection set: {err}", operationResult.GetConcatenatedMessages());
	}

	public static string MakeProjectFilename(string outputDirectory, string prjName)
	{
		return Path.Combine(outputDirectory, Sanitize(prjName)) + ".ras";
	}

	public static string Sanitize(string input)
	{
		return input.Replace("\"", "_").Replace("\\", "_").Replace("/", "_");
	}

	private Project()
	{
		InitEmpty();
	}

	public Project(string filename)
		: this(filename, null)
	{
	}

	public Project(string filename, Func<string, bool>? includeSourceFile)
	{
		if (!File.Exists(filename))
		{
			if (!Directory.Exists(filename))
			{
				throw new FileNotFoundException(filename);
			}
			string[] files = Directory.GetFiles(filename, "*.ras");
			if (files.Length == 0)
			{
				throw new ArgumentException("No RAS project files found in the given directory: *.ras");
			}
			if (files.Length != 1)
			{
				throw new ArgumentException("Provided directory has multiple RAS project files: *.ras");
			}
			filename = files[0];
		}
		Filename = filename;
		_name = Path.GetFileNameWithoutExtension(Filename);
		_includeSourceFile = includeSourceFile;
		ReloadProject();
	}

	public static Project CreateNew(string filename, UnitSystem units)
	{
		return CreateNew(filename, ProjectionDefinition.Local(units));
	}

	public static Project CreateNew(string filename, ProjectionDefinition projection)
	{
		if (string.IsNullOrEmpty(filename))
		{
			throw new ArgumentNullException("filename");
		}
		if (File.Exists(filename))
		{
			throw new Exception(filename + " must not exist.");
		}
		Project project = new Project();
		project.Filename = filename;
		project.ProjectionDefinition = projection;
		project.EnsureProjectDirectoryStructureExists();
		project.Save();
		return project;
	}

	public static Project CreateInMemory(string name = "", UnitSystem units = UnitSystem.USCustomary)
	{
		return new Project
		{
			Name = name,
			ProjectionDefinition = ProjectionDefinition.Local(units)
		};
	}

	private static OperationResult TryLoadFromExistingFile(string source, out Project prj)
	{
		OperationResult operationResult = IsValidProjectFile(source);
		if (!operationResult)
		{
			prj = null;
			return operationResult;
		}
		prj = new Project(source);
		return OperationResult.Success();
	}

	public static OperationResult TryLoad(string source, out Project prj)
	{
		try
		{
			if (File.Exists(source))
			{
				return TryLoadFromExistingFile(source, out prj);
			}
			if (Directory.Exists(source))
			{
				string[] files = Directory.GetFiles(source, "*.ras");
				if (files.Length == 0)
				{
					prj = null;
					return OperationResult.Fail(FileMessageCodes.GetDirectoryHasNoFilesWithExpectedExtension(source, ".ras"));
				}
				if (files.Length > 1)
				{
					prj = null;
					return OperationResult.Fail(FileMessageCodes.GetDirectoryHasMoreThanOneFileWithExpectedExtension(source, ".ras"));
				}
				return TryLoadFromExistingFile(files[0], out prj);
			}
			prj = null;
			return OperationResult.Fail(FileMessageCodes.GetFileOrDirectoryNotFound(source));
		}
		catch (Exception ex)
		{
			prj = null;
			return OperationResult.Fail(GeneralMessageCodes.GetExceptionOccurred("Loading project", ex));
		}
	}

	public static Project LoadFromXML(XElement el)
	{
		Project project = new Project();
		project.LoadXML(el);
		return project;
	}

	public void Save(bool force = false, ProgressReporter pr = null)
	{
		if (IsInMemory)
		{
			throw new NotSupportedException("Cannot save an in-memory project. Please use SaveAs.");
		}
		if (string.IsNullOrEmpty(Filename))
		{
			throw new ArgumentNullException("Filename");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		List<Exception> failures = new List<Exception>();
		pr.ReportProgress(5);
		Stopwatch stopwatch = Stopwatch.StartNew();
		foreach (Geometry geometry in Geometries)
		{
			try
			{
				if (force || geometry.NeedsSave())
				{
					geometry.Save();
				}
			}
			catch (Exception exception)
			{
				failures.Add(exception);
				_logger.LogWarning(exception, "Failed to save Geometry {Name}", geometry.Name);
			}
		}
		foreach (Plan plan in Plans)
		{
			try
			{
				if (force || plan.NeedsSave())
				{
					plan.Save();
				}
			}
			catch (Exception exception2)
			{
				failures.Add(exception2);
				_logger.LogWarning(exception2, "Failed to save Plan {Name}", plan.Name);
			}
		}
		foreach (BoundaryCondition boundaryCondition in BoundaryConditions)
		{
			try
			{
				if (force || boundaryCondition.NeedsSave())
				{
					boundaryCondition.Save();
				}
			}
			catch (Exception exception3)
			{
				failures.Add(exception3);
				_logger.LogWarning(exception3, "Failed to save BoundaryCondition {Name}", boundaryCondition.Name);
			}
		}
		foreach (Terrain terrain in Terrains)
		{
			try
			{
				if (force || terrain.NeedsSave())
				{
					terrain.Save();
				}
			}
			catch (Exception exception4)
			{
				failures.Add(exception4);
				_logger.LogWarning(exception4, "Failed to save Terrain {Name}", terrain.Name);
			}
		}
		foreach (ClassificationLayer classificationLayer in ClassificationLayers)
		{
			try
			{
				if (force || classificationLayer.NeedsSave())
				{
					classificationLayer.Save();
				}
			}
			catch (Exception exception5)
			{
				failures.Add(exception5);
				_logger.LogWarning(exception5, "Failed to save ClassificationLayer {Name}", classificationLayer.Name);
			}
		}
		foreach (NValueLayer nValue in NValues)
		{
			try
			{
				if (force || nValue.NeedsSave())
				{
					nValue.Save();
				}
			}
			catch (Exception exception6)
			{
				failures.Add(exception6);
				_logger.LogWarning(exception6, "Failed to save NValueLayer {Name}", nValue.Name);
			}
		}
		if (failures.Count > 0) throw new AggregateException("One or more project models failed to save.", failures);
		SaveXML();
		stopwatch.Stop();
		_logger.LogInformation("Saving Project {ProjectName} took {ElapsedMs}ms", Name, stopwatch.Elapsed.TotalMilliseconds);
	}

	public string SaveAs(string directory, string projectName, ProjectExportOptions? opts = null)
	{
		if (!Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}
		if (Directory.GetDirectories(directory).Length != 0 || Directory.GetFiles(directory).Length != 0)
		{
			_logger.LogWarning("Save-As directory not empty: {Directory}", directory);
		}
		string path = Path.Combine(directory, projectName + ".ras");
		SaveCleanXML(path, BuildXML());
		string defaultGeometryDirectory = GetDefaultGeometryDirectory(directory);
		Directory.CreateDirectory(defaultGeometryDirectory);
		foreach (Geometry geometry in Geometries)
		{
			string filename = Path.Combine(defaultGeometryDirectory, geometry.Name + ".h5");
			geometry.SaveAs(filename);
		}
		string defaultBoundaryConditionDirectory = GetDefaultBoundaryConditionDirectory(directory);
		Directory.CreateDirectory(defaultBoundaryConditionDirectory);
		foreach (BoundaryCondition boundaryCondition in BoundaryConditions)
		{
			string filename2 = Path.Combine(defaultBoundaryConditionDirectory, boundaryCondition.Name + ".h5");
			boundaryCondition.SaveAs(filename2);
		}
		string defaultPlanDirectory = GetDefaultPlanDirectory(directory);
		Directory.CreateDirectory(defaultPlanDirectory);
		foreach (Plan plan in Plans)
		{
			string filename3 = Path.Combine(defaultPlanDirectory, plan.Name + ".h5");
			plan.SaveAs(filename3);
		}
		string defaultTerrainsDirectory = GetDefaultTerrainsDirectory(directory);
		Directory.CreateDirectory(defaultTerrainsDirectory);
		foreach (Terrain terrain in Terrains)
		{
			string path2 = Path.Combine(defaultTerrainsDirectory, terrain.Name + ".h5");
			if (string.IsNullOrEmpty(terrain.Filename) || !Path.GetFullPath(terrain.Filename).RoughlyEquals(Path.GetFullPath(path2)))
			{
				terrain.ExportFullCopy(path2);
			}
		}
		string defaultClassificationLayersDirectory = GetDefaultClassificationLayersDirectory(directory);
		Directory.CreateDirectory(defaultClassificationLayersDirectory);
		foreach (ClassificationLayer classificationLayer in ClassificationLayers)
		{
			string filename4 = Path.Combine(defaultClassificationLayersDirectory, classificationLayer.Name + ".h5");
			classificationLayer.SaveAs(filename4);
		}
		string defaultNValueLayersDirectory = GetDefaultNValueLayersDirectory(directory);
		Directory.CreateDirectory(defaultNValueLayersDirectory);
		foreach (NValueLayer nValue in NValues)
		{
			string filename5 = Path.Combine(defaultNValueLayersDirectory, nValue.Name + ".h5");
			nValue.SaveAs(filename5);
		}
		return path;
	}

	internal XElement BuildXML()
	{
		XElement xElement = new XElement("Project");
		if (ProjectionDefinition != null)
		{
			XElement xElement2 = new XElement("Projection");
			xElement.Add(xElement2);
			ProjectionDefinition.Serialize(xElement2);
		}
		if (!string.IsNullOrEmpty(Description))
		{
			XElement xElement3 = new XElement("Description");
			xElement.Add(xElement3);
			xElement3.Value = Description;
		}
		if (_defaultTerrain != null)
		{
			XElement xElement4 = new XElement("DefaultTerrain");
			xElement.Add(xElement4);
			xElement4.Value = _defaultTerrain.Name;
		}
		List<KeyValuePair<Plan, PlanAssociation>> planAssociationsIfNotDefault = GetPlanAssociationsIfNotDefault();
		if (planAssociationsIfNotDefault.Count > 0)
		{
			XElement xElement5 = new XElement("PlanAssociations");
			xElement.Add(xElement5);
			foreach (KeyValuePair<Plan, PlanAssociation> planAssociationsIfNotDefault2 in planAssociationsIfNotDefault)
			{
				XElement xElement6 = new XElement("PlanAssociation");
				xElement5.Add(xElement6);
				xElement6.SetAttributeValue("Plan", planAssociationsIfNotDefault2.Key.Name);
				xElement6.SetAttributeValue("Geometry", planAssociationsIfNotDefault2.Value.GeometryName);
				xElement6.SetAttributeValue("BoundaryCondition", planAssociationsIfNotDefault2.Value.BoundaryConditionName);
			}
		}
		List<KeyValuePair<Geometry, GeometryAssociation>> geometryAssociationsIfNotDefault = GetGeometryAssociationsIfNotDefault();
		if (geometryAssociationsIfNotDefault.Count > 0)
		{
			XElement xElement7 = new XElement("GeometryAssociations");
			xElement.Add(xElement7);
			foreach (KeyValuePair<Geometry, GeometryAssociation> geometryAssociationsIfNotDefault2 in geometryAssociationsIfNotDefault)
			{
				XElement xElement8 = new XElement("GeometryAssociation");
				xElement7.Add(xElement8);
				xElement8.SetAttributeValue("Geometry", geometryAssociationsIfNotDefault2.Key.Name);
				if (!string.IsNullOrEmpty(geometryAssociationsIfNotDefault2.Value.TerrainName))
				{
					xElement8.SetAttributeValue("Terrain", geometryAssociationsIfNotDefault2.Value.TerrainName);
				}
				if (!string.IsNullOrEmpty(geometryAssociationsIfNotDefault2.Value.NValueLayerName))
				{
					xElement8.SetAttributeValue("N_Values", geometryAssociationsIfNotDefault2.Value.NValueLayerName);
				}
			}
		}
		List<KeyValuePair<Result, ResultAssociation>> resultAssociationsIfNotDefault = GetResultAssociationsIfNotDefault();
		if (resultAssociationsIfNotDefault.Count > 0)
		{
			XElement xElement9 = new XElement("ResultAssociations");
			xElement.Add(xElement9);
			foreach (KeyValuePair<Result, ResultAssociation> resultAssociationsIfNotDefault2 in resultAssociationsIfNotDefault)
			{
				XElement xElement10 = new XElement("ResultAssociation");
				xElement9.Add(xElement10);
				xElement10.SetAttributeValue("Result", resultAssociationsIfNotDefault2.Key.Name);
				if (!string.IsNullOrEmpty(resultAssociationsIfNotDefault2.Value.OverrideTerrainName))
				{
					xElement10.SetAttributeValue("OverrideTerrain", resultAssociationsIfNotDefault2.Value.OverrideTerrainName);
				}
			}
		}
		if (_connections.Count > 0)
		{
			XElement xElement11 = new XElement("Connections");
			xElement.Add(xElement11);
			_connections.SaveXML(xElement11);
		}
		if (CleanupPolicy != null)
		{
			CleanupPolicy.SaveXML(xElement);
			xElement.Element("CleanupPolicy")?.SetAttributeValue("CleanAuxiliary", CleanResultAuxiliaryFiles);
		}
		return xElement;
	}

	private void SaveXML()
	{
		XElement el = BuildXML();
		SaveCleanXML(Filename, el);
	}

	private static void SaveCleanXML(string filename, XElement el)
	{
		XmlWriterSettings settings = new XmlWriterSettings
		{
			OmitXmlDeclaration = true,
			Indent = true
		};
		using XmlWriter writer = XmlWriter.Create(filename, settings);
		el.Save(writer);
	}

	public void ApplyEditorMetadata(XElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		if (!element.Name.ToString().RoughlyEquals(Keys.Project)) throw new ArgumentException("Expected Project metadata.", nameof(element));
		_planAssociations = Plans.ToDictionary(plan => plan, _ => PlanAssociation.CreateNew(this));
		_geomAssociations = Geometries.ToDictionary(geometry => geometry, _ => GeometryAssociation.CreateNew(this));
		_resultAssociations.Clear();
		_defaultTerrain = null;
		_connections = new ExternalConnectionCollection(Filename);
		LoadXML(element);
	}

	private void LoadXML(XElement element, bool suppressWarnings = false)
	{
		XElement xElement = null;
		xElement = ((!element.Name.ToString().RoughlyEquals("Project")) ? GetChild(element, "Project") : element);
		if (xElement == null)
		{
			return;
		}
		XElement xElement2 = GetChild(xElement, "Projection");
		if (xElement2 != null)
		{
			string projType = xElement2.Attribute("Type")?.Value ?? "";
			string xElement2Term = xElement2.Value ?? "";
			ProjectionType type = ProjectionDefinition.TryParseProjectionType(projType);
			ProjectionDefinition = new ProjectionDefinition(type, xElement2Term);
		}
		XElement xElement3 = GetChild(xElement, "Description");
		if (xElement3 != null)
		{
			Description = xElement3.Value;
		}
		XElement xElement4 = GetChild(xElement, "DefaultTerrain");
		if (xElement4 != null)
		{
			string name = xElement4.Value;
			Terrain terrain = Terrains.FirstOrDefault((Terrain t) => t.Name == name);
			if (terrain != null)
			{
				_defaultTerrain = terrain;
			}
			else if (!suppressWarnings)
			{
				_logger.LogInformation("Could not locate user default terrain {name}", name);
			}
		}
		XElement xElement5 = GetChild(xElement, "PlanAssociations");
		if (xElement5 != null)
		{
			foreach (XElement child in GetChildren(xElement5, "PlanAssociation"))
			{
				string planName = child.Attribute("Plan")?.Value ?? "";
				string geomName = child.Attribute("Geometry")?.Value ?? "";
				string bcName = child.Attribute("BoundaryCondition")?.Value ?? "";
				if (string.IsNullOrEmpty(planName))
				{
					if (!suppressWarnings)
					{
						_logger.LogWarning("Malformed Plan Association - no plan name.");
					}
					continue;
				}
				Plan plan = Plans.FirstOrDefault((Plan p) => p.Name == planName);
				if (plan == null)
				{
					if (!suppressWarnings)
					{
						_logger.LogWarning("Malformed Plan Association - plan name does not exist.");
					}
				}
				else
				{
					_planAssociations[plan] = new PlanAssociation(this, geomName, bcName);
				}
			}
		}
		XElement xElement6 = GetChild(xElement, "GeometryAssociations");
		if (xElement6 != null)
		{
			foreach (XElement child2 in GetChildren(xElement6, "GeometryAssociation"))
			{
				string geomName2 = child2.Attribute("Geometry")?.Value ?? "";
				string terrName = child2.Attribute("Terrain")?.Value ?? "";
				string nvName = child2.Attribute("N_Values")?.Value ?? "";
				if (string.IsNullOrEmpty(geomName2))
				{
					if (!suppressWarnings)
					{
						_logger.LogWarning("Malformed Geometry Association - no geometry name.");
					}
					continue;
				}
				Geometry geometry = Geometries.FirstOrDefault((Geometry gg) => gg.Name == geomName2);
				if (geometry == null)
				{
					if (!suppressWarnings)
					{
						_logger.LogWarning("Malformed Geometry Association - geometry name does not exist.");
					}
				}
				else
				{
					_geomAssociations[geometry] = new GeometryAssociation(this, terrName, nvName);
				}
			}
		}
		XElement xElement7 = GetChild(xElement, "ResultAssociations");
		if (xElement7 != null)
		{
			foreach (XElement child3 in GetChildren(xElement7, "ResultAssociation"))
			{
				string resultName = child3.Attribute("Result")?.Value ?? "";
				string overrideTerrainName = child3.Attribute("OverrideTerrain")?.Value ?? "";
				if (string.IsNullOrEmpty(resultName))
				{
					if (!suppressWarnings)
					{
						_logger.LogWarning("Malformed Result Association - no result name.");
					}
					continue;
				}
				Result result = Results.FirstOrDefault((Result res) => res.Name == resultName);
				if (result == null)
				{
					if (!suppressWarnings)
					{
						_logger.LogWarning("Malformed Result Association - result '{ResultName}' does not exist.", resultName);
					}
				}
				else
				{
					_resultAssociations[result] = new ResultAssociation(this, overrideTerrainName);
				}
			}
		}
		XElement xElement8 = GetChild(xElement, "Connections");
		if (xElement8 != null)
		{
			_connections.LoadXML(xElement8);
			_connections.TryReconcileAll();
		}
		XElement xElement9 = GetChild(xElement, "CleanupPolicy");
		if (xElement9 != null)
		{
			CleanupPolicy = CleanupPolicy.LoadXML(xElement9);
			if (bool.TryParse(xElement9.Attribute("CleanAuxiliary")?.Value, out var result2))
			{
				CleanResultAuxiliaryFiles = result2;
			}
		}
		static XElement GetChild(XElement x, string key)
		{
			return x.Elements().FirstOrDefault((XElement n) => n.Name.ToString().RoughlyEquals(key));
		}
		static IEnumerable<XElement> GetChildren(XElement x, string key)
		{
			return from n in x.Elements()
				where n.Name.ToString().RoughlyEquals(key)
				select n;
		}
	}

	public void ReloadProject()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		LoadAllTerrainsInProject();
		LoadAllGeometriesInProject();
		LoadAllEventsInProject();
		LoadAllPlansInProject();
		LoadAllResultsInProject();
		LoadAllSurfaceLayersInProject();
		_hydraulicDesign = new HydraulicDesignData(this);
		HydraulicDesign.LoadAllHydraulicDesignData();
		_planAssociations = new Dictionary<Plan, PlanAssociation>();
		for (int i = 0; i < _plans.Count; i++)
		{
			Plan key = _plans[i];
			_planAssociations[key] = PlanAssociation.CreateNew(this);
		}
		_geomAssociations = new Dictionary<Geometry, GeometryAssociation>();
		for (int j = 0; j < _geometries.Count; j++)
		{
			Geometry key2 = _geometries[j];
			_geomAssociations[key2] = GeometryAssociation.CreateNew(this);
		}
		_resultAssociations = new Dictionary<Result, ResultAssociation>();
		_connections = new ExternalConnectionCollection(Filename);
		LoadXML(XElement.Load(Filename));
		stopwatch.Stop();
		_logger?.LogInformation("Project \"{Name}\" loaded in {Duration} ms.", _name, stopwatch.ElapsedMilliseconds);
	}

	private void InitEmpty()
	{
		_terrains = new List<Terrain>();
		_nValueLayers = new List<NValueLayer>();
		_plans = new List<Plan>();
		_results = new List<Result>();
		_geometries = new List<Geometry>();
		_boundaryConditions = new List<BoundaryCondition>();
		_classificationLayers = new List<ClassificationLayer>();
		_nValueLayers = new List<NValueLayer>();
		_connections = new ExternalConnectionCollection(Filename);
		_planAssociations = new Dictionary<Plan, PlanAssociation>();
		_geomAssociations = new Dictionary<Geometry, GeometryAssociation>();
		_resultAssociations = new Dictionary<Result, ResultAssociation>();
		_hydraulicDesign = new HydraulicDesignData(this);
		HydraulicDesign.InitEmpty();
	}

	private void LoadAllGeometriesInProject()
	{
		_geometries = Loader(GeometriesDirectory, (string fn) => new Geometry(fn, this));
	}

	private void LoadAllPlansInProject()
	{
		_plans = Loader(PlansDirectory, (string fn) => new Plan(fn, this));
	}

	private void LoadAllResultsInProject()
	{
		_results = Loader(ResultsDirectory, (string fn) =>
		{
			OperationResult operationResult = Result.TryLoad(fn, out var res);
			if ((bool)operationResult)
			{
				res.Project = this;
				return res;
			}
			_logger.LogWarning("Could not load file.\n{Message}", operationResult.GetConcatenatedMessages());
			return (Result)null;
		});
		// Existing HEC-RAS projects also store Results with the .hdf suffix.
		// Keep the same request-scoped pre-open predicate for both formats.
		_results.AddRange(Loader(ResultsDirectory, (string fn) =>
		{
			OperationResult operationResult = Result.TryLoad(fn, out var res);
			if ((bool)operationResult)
			{
				res.Project = this;
				return res;
			}
			_logger.LogWarning("Could not load file.\n{Message}", operationResult.GetConcatenatedMessages());
			return (Result)null;
		}, "*.hdf"));
	}

	private void LoadAllEventsInProject()
	{
		_boundaryConditions = Loader(BoundaryConditionDirectory, (string fn) => new BoundaryCondition(fn, this));
	}

	private void LoadAllTerrainsInProject()
	{
		_terrains = Loader(TerrainsDirectory, (string fn) => new Terrain(fn, this));
		_terrains.AddRange(Loader(TerrainsDirectory, Terrain.FromS3, "*.s3"));
	}

	private void LoadAllSurfaceLayersInProject()
	{
		_nValueLayers = new List<NValueLayer>();
		_classificationLayers = new List<ClassificationLayer>();
		if (IsInMemory)
		{
			return;
		}
		string surfaceLayersDirectory = SurfaceLayersDirectory;
		if (!Directory.Exists(surfaceLayersDirectory))
		{
			return;
		}
		string searchPattern = "*.h5";
		string[] files = Directory.GetFiles(surfaceLayersDirectory, searchPattern);
		foreach (string textValue in files)
		{
			if (!ShouldLoadSourceFile(textValue))
			{
				continue;
			}
			try
			{
				using H5Reader hr = new H5Reader(textValue);
				if (NValueLayer.IsValid(hr))
				{
					NValueLayer nValueLayer = new NValueLayer(textValue, this);
					_nValueLayers.Add(nValueLayer);
				}
				else if (ClassificationLayer.IsValid(hr))
				{
					ClassificationLayer classificationLayer = new ClassificationLayer(textValue, this);
					_classificationLayers.Add(classificationLayer);
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Could not load core file {Filename}", textValue);
			}
		}
	}

	public bool TryEstablishProjectionFromLayers()
	{
		if (Projection != null)
		{
			return true;
		}
		foreach (Terrain terrain in Terrains)
		{
			foreach (string textValue in from rti in terrain.AllRasterInputs()
				select rti.Filename)
			{
				if (!File.Exists(textValue))
				{
					continue;
				}
				Projection projection = GDALRaster.GetProjection(textValue);
				if (projection == null)
				{
					continue;
				}
				string wKT = projection.GetWKT();
				if (!string.IsNullOrEmpty(wKT))
				{
					ProjectionDefinition = new ProjectionDefinition(ProjectionType.WKT, wKT);
					if (Projection != null)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	internal List<T> Loader<T>(string dir, Func<string, T> makeFromFile, string extensionSearchPattern = "*.h5")
	{
		List<T> items = new List<T>();
		if (IsInMemory)
		{
			return items;
		}
		if (!Directory.Exists(dir))
		{
			return items;
		}
		string[] files = Directory.GetFiles(dir, extensionSearchPattern);
		foreach (string textValue in files)
		{
			if (!ShouldLoadSourceFile(textValue))
			{
				continue;
			}
			try
			{
				T makeFromFile2 = makeFromFile(textValue);
				if (makeFromFile2 == null)
				{
					_logger.LogWarning("Could not create layer from {Filename}", textValue);
				}
				else
				{
					items.Add(makeFromFile2);
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Could not load core file {Filename}", textValue);
			}
		}
		return items;
	}

	private bool ShouldLoadSourceFile(string filename)
	{
		return _includeSourceFile == null || _includeSourceFile(filename);
	}

	internal List<Task<T>> LoaderAsync<T>(string dir, Func<string, Task<T>> makeFromFile, string extensionSearchPattern = "*.h5")
	{
		List<Task<T>> taskList = new List<Task<T>>();
		if (IsInMemory)
		{
			return taskList;
		}
		if (!Directory.Exists(dir))
		{
			return taskList;
		}
		string[] files = Directory.GetFiles(dir, extensionSearchPattern);
		foreach (string textValue in files)
		{
			if (!ShouldLoadSourceFile(textValue))
			{
				continue;
			}
			try
			{
				Task<T> task = makeFromFile(textValue);
				if (task == null)
				{
					_logger.LogWarning("Could not create layer from {Filename}", textValue);
				}
				else
				{
					taskList.Add(task);
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "Could not load core file {Filename}", textValue);
			}
		}
		return taskList;
	}

	public void SaveProjectMetadataToH5Header(H5Writer writer)
	{
		writer.WriteAttribute("/", "Project Name", Name);
		if (Projection != null)
		{
			string wKT = Projection.GetWKT();
			writer.WriteAttribute("/", "Project Projection", wKT);
		}
		writer.WriteAttribute("/", "Project Units", ProjectionDefinition.SerializeUnitSystemShort(Units));
	}

	public static void MigrateProjectMetadata(string source, string target)
	{
		using H5Reader source2 = new H5Reader(source);
		using H5Writer target2 = new H5Writer(target);
		MigrateProjectMetadata(source2, target2);
	}

	public static void MigrateProjectMetadata(H5Reader source, H5Writer target)
	{
		string tryReadStringAttributeScalar = source.TryReadStringAttributeScalar("/", "Project Name");
		string tryReadStringAttributeScalar2 = source.TryReadStringAttributeScalar("/", "Project Projection");
		string tryReadStringAttributeScalar3 = source.TryReadStringAttributeScalar("/", "Project Units");
		if (!string.IsNullOrEmpty(tryReadStringAttributeScalar))
		{
			target.WriteAttribute("/", "Project Name", tryReadStringAttributeScalar);
		}
		if (!string.IsNullOrEmpty(tryReadStringAttributeScalar2))
		{
			target.WriteAttribute("/", "Project Projection", tryReadStringAttributeScalar2);
		}
		if (!string.IsNullOrEmpty(tryReadStringAttributeScalar3))
		{
			target.WriteAttribute("/", "Project Units", tryReadStringAttributeScalar3);
		}
	}

	public static void LoadProjectMetadataFromH5Header(H5Reader hr, out string projectName, out string wkt, out UnitSystem units)
	{
		projectName = hr.TryReadStringAttributeScalar("/", "Project Name");
		wkt = hr.TryReadStringAttributeScalar("/", "Project Projection");
		string tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar("/", "Project Units");
		if (string.IsNullOrEmpty(projectName))
		{
			projectName = hr.TryReadStringAttributeScalar("/", "PROJECT_NAME");
		}
		if (string.IsNullOrEmpty(wkt))
		{
			wkt = hr.TryReadStringAttributeScalar("/", "PROJECT_PROJECTION");
		}
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
		{
			tryReadStringAttributeScalar = hr.TryReadStringAttributeScalar("/", "PROJECT_UNITS");
		}
		if (string.IsNullOrEmpty(tryReadStringAttributeScalar))
		{
			units = UnitSystem.USCustomary;
		}
		else if (!ProjectionDefinition.TryParseUnitSystem(tryReadStringAttributeScalar, out units))
		{
			units = UnitSystem.USCustomary;
		}
	}

	public static void LoadProjectMetadataFromH5Header(H5Reader hr, out string projectName, out Projection projection, out UnitSystem units)
	{
		LoadProjectMetadataFromH5Header(hr, out projectName, out string wkt, out units);
		if (string.IsNullOrEmpty(wkt))
		{
			projection = null;
			return;
		}
		try
		{
			projection = new WKTProjection(wkt);
		}
		catch (Exception exception)
		{
			projection = null;
			_logger.LogWarning(exception, "Could not parse WKT");
		}
	}

	public Plan AddNewPlan(string planName)
	{
		if (IsInMemory)
		{
			Plan plan = Plan.CreateInMemory(planName);
			AddPlan(plan);
			return plan;
		}
		Plan plan2 = Plan.CreateNew(Path.Combine(PlansDirectory, planName + ".h5"));
		AddPlan(plan2);
		return plan2;
	}

	public Plan AddNewPlan(string planName, Geometry geom, BoundaryCondition bc)
	{
		Plan plan = AddNewPlan(planName);
		PlanAssociation planAssociation = plan.AssertGetPlanAssocation();
		planAssociation.Geometry = geom;
		planAssociation.BoundaryCondition = bc;
		return plan;
	}

	public void AddPlan(Plan p)
	{
		p.Project = this;
		_plans.Add(p);
		_planAssociations[p] = PlanAssociation.CreateNew(this);
	}

	public Plan ImportPlan(string filename)
	{
		Plan plan = new Plan(ImportSafe(filename, PlansDirectory, ".h5"));
		AddPlan(plan);
		return plan;
	}

	public void RemovePlan(Plan p)
	{
		_plans.Remove(p);
		_planAssociations.Remove(p);
	}

	public Plan TryRemovePlanByFilename(string fn)
	{
		Plan plan = TryFindPlanByFilename(fn);
		if (plan == null)
		{
			return null;
		}
		RemovePlan(plan);
		return plan;
	}

	public Plan ClonePlan(Plan planBase, string newName)
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), newName + ".h5");
		planBase.SaveAs(path);
		Plan plan = ImportPlan(path);
		PlanAssociation planAssociation = GetPlanAssociation(planBase);
		if (planAssociation != null)
		{
			PlanAssociation planAssociation2 = GetPlanAssociation(plan);
			planAssociation2.Geometry = planAssociation.Geometry;
			planAssociation2.BoundaryCondition = planAssociation.BoundaryCondition;
		}
		File.Delete(path);
		return plan;
	}

	public Plan FindPlan(string planName)
	{
		return Plans.FirstOrDefault((Plan p) => p.Name.RoughlyEquals(planName));
	}

	public Plan TryFindPlanByFilename(string fn)
	{
		return Plans.FirstOrDefault((Plan bc) => bc.Filename.RoughlyEquals(fn));
	}

	public Geometry AddNewGeometry(string geomName, bool autoSanitizeName = false)
	{
		geomName = StringHelpers.NextUniqueName(Geometries, (Geometry g) => g.Name, geomName);
		Geometry geometry = CreateNewGeometry(geomName);
		AddGeometry(geometry);
		return geometry;
	}

	public Geometry AddNewGeometry(string geomName, Terrain tr, NValueLayer nvl)
	{
		Geometry geometry = AddNewGeometry(geomName);
		GeometryAssociation geometryAssociation = geometry.GeometryAssociation;
		geometryAssociation.Terrain = tr;
		geometryAssociation.NValueLayer = nvl;
		return geometry;
	}

	private Geometry CreateNewGeometry(string geomName)
	{
		if (IsInMemory)
		{
			Geometry geometry = Geometry.CreateInMemory(geomName);
			AddGeometry(geometry);
			return geometry;
		}
		if (string.IsNullOrEmpty(geomName))
		{
			throw new ArgumentNullException();
		}
		Geometry geometry2 = Geometry.CreateNew(Path.Combine(GeometriesDirectory, geomName + ".h5"));
		AddGeometry(geometry2);
		return geometry2;
	}

	public void AddGeometry(Geometry geom)
	{
		geom.Project = this;
		if (!_geometries.Contains(geom))
		{
			_geometries.Add(geom);
		}
		EnsureAssociation(geom);
	}

	public Geometry ImportGeometry(string filename)
	{
		Geometry geometry = new Geometry(ImportSafe(filename, GeometriesDirectory, ".h5"));
		AddGeometry(geometry);
		return geometry;
	}

	private GeometryAssociation EnsureAssociation(Geometry geom)
	{
		if (_geomAssociations.TryGetValue(geom, out var geomAssociations))
		{
			return geomAssociations;
		}
		geomAssociations = GeometryAssociation.CreateNew(this);
		_geomAssociations[geom] = geomAssociations;
		return geomAssociations;
	}

	public Geometry FindGeometry(string geomName)
	{
		return Geometries.FirstOrDefault((Geometry g) => g.Name.RoughlyEquals(geomName));
	}

	public Geometry TryFindGeometryByFilename(string fn)
	{
		for (int i = 0; i < _geometries.Count; i++)
		{
			Geometry geometry = _geometries[i];
			if (geometry.Filename.RoughlyEquals(fn))
			{
				return geometry;
			}
		}
		return null;
	}

	public Geometry TryFindGeometryByName(string name)
	{
		for (int i = 0; i < _geometries.Count; i++)
		{
			Geometry geometry = _geometries[i];
			if (geometry.Name == name)
			{
				return geometry;
			}
		}
		return null;
	}

	public Geometry FindNearestGeometryByName(string name)
	{
		float match = 0f;
		Geometry result = null;
		for (int i = 0; i < _geometries.Count; i++)
		{
			Geometry geometry = _geometries[i];
			if (geometry.Name == name)
			{
				return geometry;
			}
			float match2 = FuzzyStringMatcher.Match(name, geometry.Name);
			if (match2 > match)
			{
				result = geometry;
				match = match2;
			}
		}
		return result;
	}

	public Geometry TryRemoveGeometryByFilename(string fn)
	{
		Geometry geometry = TryFindGeometryByFilename(fn);
		if (geometry == null)
		{
			return null;
		}
		RemoveGeometry(geometry);
		return geometry;
	}

	public Geometry TryRemoveGeometryByName(string name)
	{
		Geometry geometry = null;
		for (int i = 0; i < _geometries.Count; i++)
		{
			if (_geometries[i].Name.RoughlyEquals(name))
			{
				geometry = _geometries[i];
				break;
			}
		}
		if (geometry == null)
		{
			return null;
		}
		RemoveGeometry(geometry);
		return geometry;
	}

	public void RemoveGeometry(Geometry g)
	{
		foreach (PlanAssociation planAssociation in _planAssociations.Values)
		{
			if (planAssociation.Geometry == g)
			{
				planAssociation.AlertGeometryRemoved();
			}
		}
		_geometries.Remove(g);
		_geomAssociations.Remove(g);
	}

	public BoundaryCondition AddNewBoundaryCondition(string bcName)
	{
		if (IsInMemory)
		{
			BoundaryCondition boundaryCondition = BoundaryCondition.CreateInMemory(bcName);
			boundaryCondition.Project = this;
			_boundaryConditions.Add(boundaryCondition);
			return boundaryCondition;
		}
		BoundaryCondition boundaryCondition2 = BoundaryCondition.CreateNew(Path.Combine(BoundaryConditionDirectory, bcName + ".h5"));
		boundaryCondition2.Project = this;
		_boundaryConditions.Add(boundaryCondition2);
		return boundaryCondition2;
	}

	public BoundaryCondition AddNewBoundaryCondition(string bcName, DateTime startTime, DateTime endTime)
	{
		BoundaryCondition boundaryCondition = AddNewBoundaryCondition(bcName);
		boundaryCondition.StartTime = startTime;
		boundaryCondition.EndTime = endTime;
		return boundaryCondition;
	}

	public BoundaryCondition AddNewBoundaryCondition(string bcName, DateTime startTime, TimeSpan duration)
	{
		BoundaryCondition boundaryCondition = AddNewBoundaryCondition(bcName);
		boundaryCondition.StartTime = startTime;
		boundaryCondition.EndTime = startTime.Add(duration);
		return boundaryCondition;
	}

	public void AddBoundaryCondition(BoundaryCondition bc)
	{
		bc.Project = this;
		_boundaryConditions.Add(bc);
	}

	public BoundaryCondition ImportBoundaryCondition(string filename)
	{
		BoundaryCondition boundaryCondition = new BoundaryCondition(ImportSafe(filename, BoundaryConditionDirectory, ".h5"));
		AddBoundaryCondition(boundaryCondition);
		return boundaryCondition;
	}

	public BoundaryCondition CloneBoundaryCondition(BoundaryCondition bcBase, string newName)
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), newName + ".h5");
		bcBase.SaveAs(path);
		BoundaryCondition result = ImportBoundaryCondition(path);
		File.Delete(path);
		return result;
	}

	public void RemoveBoundaryCondition(BoundaryCondition bc)
	{
		_boundaryConditions.Remove(bc);
	}

	public BoundaryCondition TryRemoveBoundaryConditionByFilename(string fn)
	{
		BoundaryCondition boundaryCondition = TryFindBoundaryConditionByFilename(fn);
		if (boundaryCondition == null)
		{
			return null;
		}
		RemoveBoundaryCondition(boundaryCondition);
		return boundaryCondition;
	}

	public BoundaryCondition TryFindBoundaryConditionByFilename(string fn)
	{
		return _boundaryConditions.FirstOrDefault((BoundaryCondition bc) => bc.Filename.RoughlyEquals(fn));
	}

	public BoundaryCondition FindBoundaryConditions(string bcName)
	{
		return BoundaryConditions.FirstOrDefault((BoundaryCondition bc) => bc.Name.RoughlyEquals(bcName));
	}

	public void AddResult(Result res)
	{
		_results.Add(res);
		res.Project = this;
	}

	public void RemoveResult(Result r)
	{
		_results.Remove(r);
	}

	public List<Result> FindResultsByPlanName(string planName)
	{
		List<Result> results = new List<Result>();
		foreach (Result result in Results)
		{
			if (result.Plan.Name.RoughlyEquals(planName))
			{
				results.Add(result);
			}
		}
		return results.OrderBy((Result r) => r.ComputeSummary.EndTime).ToList();
	}

	public string GetUniqueResultFilename(string planName)
	{
		return FileHelpers.UniqueFilename(Path.Combine(ResultsDirectory, planName + ".h5"));
	}

	public Terrain AddNewTerrain(string trName)
	{
		if (IsInMemory)
		{
			Terrain terrain = Terrain.CreateInMemory(trName);
			_terrains.Add(terrain);
			return terrain;
		}
		Terrain terrain2 = Terrain.CreateNew(Path.Combine(TerrainsDirectory, trName + ".h5"));
		_terrains.Add(terrain2);
		return terrain2;
	}

	public void AddTerrain(Terrain terr)
	{
		if (!_terrains.Contains(terr))
		{
			_terrains.Add(terr);
		}
	}

	public void RemoveTerrain(Terrain terr)
	{
		terr.Project = null;
		_terrains.Remove(terr);
		foreach (KeyValuePair<Geometry, GeometryAssociation> geomAssociation in _geomAssociations)
		{
			if (geomAssociation.Value.Terrain == terr)
			{
				geomAssociation.Value.Terrain = null;
			}
		}
		foreach (KeyValuePair<Result, ResultAssociation> resultAssociation in _resultAssociations)
		{
			if (resultAssociation.Value.OverrideTerrain == terr)
			{
				resultAssociation.Value.OverrideTerrain = null;
			}
		}
	}

	public Terrain FindTerrain(string terrName)
	{
		return Terrains.FirstOrDefault((Terrain t) => t.Name == terrName);
	}

	public Terrain TryFindTerrainByFilename(string fn)
	{
		for (int i = 0; i < _terrains.Count; i++)
		{
			Terrain terrain = _terrains[i];
			if (terrain.Filename.RoughlyEquals(fn))
			{
				return terrain;
			}
		}
		return null;
	}

	public Terrain TryFindTerrainByName(string name)
	{
		for (int i = 0; i < _terrains.Count; i++)
		{
			Terrain terrain = _terrains[i];
			if (terrain.Name == name)
			{
				return terrain;
			}
		}
		return null;
	}

	public Terrain TryRemoveTerrainByFilename(string fn)
	{
		Terrain terrain = TryFindTerrainByFilename(fn);
		if (terrain == null)
		{
			return null;
		}
		RemoveTerrain(terrain);
		return terrain;
	}

	public ClassificationLayer AddNewClassification(string name)
	{
		if (IsInMemory)
		{
			ClassificationLayer classificationLayer = ClassificationLayer.CreateInMemory(name);
			AddClassification(classificationLayer);
			return classificationLayer;
		}
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException();
		}
		ClassificationLayer classificationLayer2 = ClassificationLayer.CreateNew(Path.Combine(ClassificationLayersDirectory, name + ".h5"));
		AddClassification(classificationLayer2);
		return classificationLayer2;
	}

	public void AddClassification(ClassificationLayer lyr)
	{
		lyr.Project = this;
		if (!_classificationLayers.Contains(lyr))
		{
			_classificationLayers.Add(lyr);
		}
	}

	public ClassificationLayer TryAddNewClassification(string name)
	{
		if (IsInMemory)
		{
			ClassificationLayer classificationLayer = ClassificationLayer.CreateInMemory(name);
			AddClassification(classificationLayer);
			return classificationLayer;
		}
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException();
		}
		ClassificationLayer classificationLayer2 = ClassificationLayer.CreateNew(FileHelpers.UniqueFilename(Path.Combine(ClassificationLayersDirectory, name + ".h5")));
		AddClassification(classificationLayer2);
		return classificationLayer2;
	}

	public ClassificationLayer ImportClassification(string filename)
	{
		throw new NotImplementedException();
	}

	public ClassificationLayer FindClassification(string name)
	{
		return ClassificationLayers.FirstOrDefault((ClassificationLayer lyr) => lyr.Name.RoughlyEquals(name));
	}

	public ClassificationLayer TryFindClassificationByFilename(string fn)
	{
		for (int i = 0; i < _classificationLayers.Count; i++)
		{
			ClassificationLayer classificationLayer = _classificationLayers[i];
			if (classificationLayer.Filename.RoughlyEquals(fn))
			{
				return classificationLayer;
			}
		}
		return null;
	}

	public ClassificationLayer TryRemoveClassificationByFilename(string fn)
	{
		ClassificationLayer classificationLayer = TryFindClassificationByFilename(fn);
		if (classificationLayer == null)
		{
			return null;
		}
		RemoveClassification(classificationLayer);
		return classificationLayer;
	}

	public ClassificationLayer TryRemoveClassificationByName(string name)
	{
		ClassificationLayer classificationLayer = null;
		for (int i = 0; i < _classificationLayers.Count; i++)
		{
			if (_classificationLayers[i].Name.RoughlyEquals(name))
			{
				classificationLayer = _classificationLayers[i];
				break;
			}
		}
		if (classificationLayer == null)
		{
			return null;
		}
		RemoveClassification(classificationLayer);
		return classificationLayer;
	}

	public void RemoveClassification(ClassificationLayer lyr)
	{
		_classificationLayers.Remove(lyr);
	}

	public void AddNValueLayer(NValueLayer lyr)
	{
		lyr.Project = this;
		if (!_nValueLayers.Contains(lyr))
		{
			_nValueLayers.Add(lyr);
		}
	}

	public NValueLayer AddNewNValueLayer(string name)
	{
		if (IsInMemory)
		{
			NValueLayer nValueLayer = NValueLayer.CreateInMemory(name);
			AddNValueLayer(nValueLayer);
			return nValueLayer;
		}
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException();
		}
		NValueLayer nValueLayer2 = NValueLayer.CreateNew(Path.Combine(NValueLayersDirectory, name + ".h5"));
		AddNValueLayer(nValueLayer2);
		return nValueLayer2;
	}

	public NValueLayer TryAddNewNValueLayer(string name)
	{
		if (IsInMemory)
		{
			NValueLayer nValueLayer = NValueLayer.CreateInMemory(name);
			AddNValueLayer(nValueLayer);
			return nValueLayer;
		}
		if (string.IsNullOrEmpty(name))
		{
			throw new ArgumentNullException();
		}
		NValueLayer nValueLayer2 = NValueLayer.CreateNew(FileHelpers.UniqueFilename(Path.Combine(NValueLayersDirectory, name + ".h5")));
		AddNValueLayer(nValueLayer2);
		return nValueLayer2;
	}

	public void DebugAddNewNValueLayer(NValueLayer nvl)
	{
		_nValueLayers.Add(nvl);
	}

	public NValueLayer ImportNValueLayer(string filename)
	{
		NValueLayer nValueLayer = new NValueLayer(ImportSafe(filename, NValueLayersDirectory, ".h5"));
		AddNValueLayer(nValueLayer);
		return nValueLayer;
	}

	public NValueLayer FindNValueLayer(string nvLyrName)
	{
		return NValues.FirstOrDefault((NValueLayer nvl) => nvl.Name == nvLyrName);
	}

	public NValueLayer TryFindNValueByFilename(string fn)
	{
		for (int i = 0; i < _nValueLayers.Count; i++)
		{
			NValueLayer nValueLayer = _nValueLayers[i];
			if (nValueLayer.Filename.RoughlyEquals(fn))
			{
				return nValueLayer;
			}
		}
		return null;
	}

	public NValueLayer TryRemoveNValueByFilename(string fn)
	{
		NValueLayer nValueLayer = TryFindNValueByFilename(fn);
		if (nValueLayer == null)
		{
			return null;
		}
		RemoveNValue(nValueLayer);
		return nValueLayer;
	}

	public NValueLayer TryRemoveNValueByName(string name)
	{
		NValueLayer nValueLayer = null;
		for (int i = 0; i < _nValueLayers.Count; i++)
		{
			if (_nValueLayers[i].Name.RoughlyEquals(name))
			{
				nValueLayer = _nValueLayers[i];
				break;
			}
		}
		if (nValueLayer == null)
		{
			return null;
		}
		RemoveNValue(nValueLayer);
		return nValueLayer;
	}

	public void RemoveNValue(NValueLayer lyr)
	{
		_nValueLayers.Remove(lyr);
	}

	public PlanAssociation GetPlanAssociation(Plan p)
	{
		if (p.IsResult)
		{
			return null;
		}
		if (_planAssociations.TryGetValue(p, out var localValue))
		{
			return localValue;
		}
		if (_plans.Contains(p))
		{
			throw new Exception("Internal error, plan association not found for '" + p.Name + "'.");
		}
		throw new Exception($"Error, plan '{p.Name}' ({p.Filename}) is not associated with project '{Name}' ({_plans.Count} plans, {_planAssociations.Count} associations).");
	}

	private List<KeyValuePair<Plan, PlanAssociation>> GetPlanAssociationsIfNotDefault()
	{
		return _planAssociations.Where((KeyValuePair<Plan, PlanAssociation> kvp) => !kvp.Value.IsDefault()).ToList();
	}

	public GeometryAssociation GetGeometryAssociation(Geometry g)
	{
		if (g.IsResult)
		{
			return null;
		}
		if (_geomAssociations.TryGetValue(g, out var localValue))
		{
			return localValue;
		}
		if (_geometries.Contains(g))
		{
			throw new Exception("Internal error, geometry association not found for '" + g.Name + "'.");
		}
		throw new Exception($"Error, geometry '{g.Name}' ({g.Filename}) is not associated with project '{Name}' ({_geometries.Count} geometries, {_geomAssociations.Count} associations).");
	}

	private List<KeyValuePair<Geometry, GeometryAssociation>> GetGeometryAssociationsIfNotDefault()
	{
		return _geomAssociations.Where((KeyValuePair<Geometry, GeometryAssociation> kvp) => !kvp.Value.IsDefault()).ToList();
	}

	public Terrain GetBestTerrainFor(Result res)
	{
		if (_resultAssociations.TryGetValue(res, out var localValue) && localValue.HasOverride)
		{
			return localValue.OverrideTerrain;
		}
		TerrainSignature resultTerrainSignature = GetResultTerrainSignature(res);
		if (resultTerrainSignature != null)
		{
			foreach (Terrain terrain2 in Terrains)
			{
				if (terrain2.GetSignature().Compare(resultTerrainSignature).Result)
				{
					return terrain2;
				}
			}
			Terrain terrain = TryFindTerrainByName(resultTerrainSignature.Name);
			if (terrain != null)
			{
				return terrain;
			}
		}
		return DefaultTerrain;
	}

	public Terrain GetExactTerrainFor(Result res)
	{
		TerrainSignature resultTerrainSignature = GetResultTerrainSignature(res);
		if (resultTerrainSignature == null)
		{
			return null;
		}
		foreach (Terrain terrain in Terrains)
		{
			if (terrain.GetSignature().Compare(resultTerrainSignature).Result)
			{
				return terrain;
			}
		}
		return null;
	}

	private TerrainSignature GetResultTerrainSignature(Result res)
	{
		FlowAreaLayer flowAreaLayer = res.Geometry?.FlowAreaLayer;
		if (flowAreaLayer == null || flowAreaLayer.Count == 0)
		{
			return null;
		}
		return flowAreaLayer[0]?.PropertyTableSignature?.TerrainSignature;
	}

	public ResultAssociation GetResultAssociation(Result res)
	{
		if (_resultAssociations.TryGetValue(res, out var localValue))
		{
			return localValue;
		}
		ResultAssociation resultAssociation = ResultAssociation.CreateNew(this);
		_resultAssociations[res] = resultAssociation;
		return resultAssociation;
	}

	public void SetResultTerrainOverride(Result res, Terrain overrideTerrain)
	{
		GetResultAssociation(res).OverrideTerrain = overrideTerrain;
	}

	public void ClearResultTerrainOverride(Result res)
	{
		if (_resultAssociations.TryGetValue(res, out var localValue))
		{
			localValue.OverrideTerrain = null;
		}
	}

	private List<KeyValuePair<Result, ResultAssociation>> GetResultAssociationsIfNotDefault()
	{
		return _resultAssociations.Where((KeyValuePair<Result, ResultAssociation> kvp) => !kvp.Value.IsDefault()).ToList();
	}

	public void ComputeAll(ProgressReporter prog = null)
	{
		if (prog == null)
		{
			prog = ProgressReporter.None();
		}
		foreach (Plan plan in Plans)
		{
			_ = plan;
		}
	}

	public ComputePackage GetComputePackage(Plan p)
	{
		PlanAssociation planAssociation = GetPlanAssociation(p);
		return new ComputePackage
		{
			Geometry = planAssociation.Geometry,
			BoundaryConditions = planAssociation.BoundaryCondition
		};
	}

	private static string ImportSafe(string sourceFile, string targetDirectory, string expectedExtension)
	{
		string path = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));
		if (!Path.HasExtension(expectedExtension))
		{
			path = Path.ChangeExtension(path, expectedExtension);
		}
		path = FileHelpers.UniqueFilename(path);
		File.Copy(sourceFile, path);
		return path;
	}

	public void EnsureProjectDirectoryStructureExists()
	{
		if (IsInMemory)
		{
			throw new NotSupportedException("IsInMemory");
		}
		Action<string> action = (string dir) =>
		{
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}
		};
		action(Path.GetDirectoryName(Filename));
		action(GeometriesDirectory);
		action(PlansDirectory);
		action(ResultsDirectory);
		action(TerrainsDirectory);
		action(BoundaryConditionDirectory);
		action(HydraulicDesignDirectory);
		action(ClassificationLayersDirectory);
	}

	public string EnsureDefaultExportDirectory()
	{
		string defaultExportDirectory = DefaultExportDirectory;
		if (!Directory.Exists(defaultExportDirectory))
		{
			Directory.CreateDirectory(defaultExportDirectory);
		}
		return defaultExportDirectory;
	}

	public string EnsureDefaultDownloadDirectory()
	{
		string defaultDownloadsDirectory = DefaultDownloadsDirectory;
		if (!Directory.Exists(defaultDownloadsDirectory))
		{
			Directory.CreateDirectory(defaultDownloadsDirectory);
		}
		return defaultDownloadsDirectory;
	}

	public string EnsureSpecificDownloadDirectory(string subType)
	{
		string path = Path.Combine(DefaultDownloadsDirectory, subType);
		if (!Directory.Exists(path))
		{
			Directory.CreateDirectory(path);
		}
		return path;
	}

	public static string GetDefaultGeometryDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Geometries");
	}

	public static string GetDefaultPlanDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Plans");
	}

	public static string GetDefaultResultDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Results");
	}

	public static string GetDefaultBoundaryConditionDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Boundary Conditions");
	}

	public static string GetDefaultTerrainsDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Terrains");
	}

	public static string GetDefaultSurfaceLayersDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Surface Layers");
	}

	public static string GetDefaultClassificationLayersDirectory(string projectDirectory)
	{
		return GetDefaultSurfaceLayersDirectory(projectDirectory);
	}

	public static string GetDefaultNValueLayersDirectory(string projectDirectory)
	{
		return GetDefaultSurfaceLayersDirectory(projectDirectory);
	}

	public static string GetDefaultHydraulicDesignDirectory(string projectDirectory)
	{
		return Path.Combine(projectDirectory, "Hydraulic Design");
	}

	public static string GetDefaultStableChannelDesignDirectory(string projectDirectory)
	{
		return GetDefaultHydraulicDesignDirectory(projectDirectory);
	}

	public static string GetDefaultDirectory<T>(string projectDirectory)
	{
		return GetDefaultDirectory(typeof(T), projectDirectory);
	}

	public static string GetDefaultDirectory(Type t, string projectDirectory)
	{
		Func<string, string> func;
		if (t == typeof(Geometry))
		{
			func = GetDefaultGeometryDirectory;
		}
		else if (t == typeof(Plan))
		{
			func = GetDefaultPlanDirectory;
		}
		else if (t == typeof(Result))
		{
			func = GetDefaultResultDirectory;
		}
		else if (t == typeof(BoundaryCondition))
		{
			func = GetDefaultBoundaryConditionDirectory;
		}
		else if (t == typeof(Terrain))
		{
			func = GetDefaultTerrainsDirectory;
		}
		else if (t == typeof(NValueLayer))
		{
			func = GetDefaultNValueLayersDirectory;
		}
		else if (t == typeof(ClassificationLayer))
		{
			func = GetDefaultClassificationLayersDirectory;
		}
		else if (t == typeof(HydraulicDesignData))
		{
			func = GetDefaultHydraulicDesignDirectory;
		}
		else
		{
			if (!(t == typeof(StableChannelDesign)))
			{
				throw new NotImplementedException();
			}
			func = GetDefaultStableChannelDesignDirectory;
		}
		return func(projectDirectory);
	}

	public static string MakeFilename<T>(string projectDirectory, string inputName, string extension)
	{
		return Path.Combine(GetDefaultDirectory<T>(projectDirectory), inputName + extension);
	}

	public static string MakeFilename(Type t, string projectDirectory, string inputName, string extension)
	{
		return Path.Combine(GetDefaultDirectory(t, projectDirectory), inputName + extension);
	}

	public static OperationResult IsValidProjectFile(string fn)
	{
		if (!File.Exists(fn))
		{
			return OperationResult.Fail(FileMessageCodes.GetFileNotFound(fn));
		}
		if (!Path.GetExtension(fn).RoughlyEquals(".ras"))
		{
			return OperationResult.Fail(FileMessageCodes.GetFileIncorrectExtension(fn, ".ras"));
		}
		return OperationResult.Success();
	}

	public static Projection TryParseProjectionFrom(string projectFile)
	{
		try
		{
			Project project = new Project();
			project.LoadXML(XElement.Load(projectFile), suppressWarnings: true);
			return project.Projection;
		}
		catch
		{
			return null;
		}
	}

	public static string TryGetProjectFilenameFromDirectory(string projectDirectory)
	{
		string[] files = Directory.GetFiles(projectDirectory, "*.ras", SearchOption.AllDirectories);
		if (files != null && files.Length == 1)
		{
			return files[0];
		}
		return "";
	}

	public static bool IsValidZippedProject(string fn)
	{
		try
		{
			using FileStream stream = new FileStream(fn, FileMode.Open, FileAccess.Read);
			using ZipArchive zipArchive = new ZipArchive(stream);
			foreach (ZipArchiveEntry entry in zipArchive.Entries)
			{
				if (Path.GetExtension(entry.FullName).RoughlyEquals(".ras"))
				{
					return true;
				}
			}
			return true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Error opening zip file in IsValidZippedProject");
			return false;
		}
	}

	public static bool IsValidCoreFile(string filename, out CoreFileType fileType)
	{
		try
		{
			using H5Reader h5r = new H5Reader(filename);
			if (Result.IsValid(h5r))
			{
				fileType = CoreFileType.Result;
				return true;
			}
			if (Plan.IsValid(h5r))
			{
				fileType = CoreFileType.Plan;
				return true;
			}
			if (Geometry.IsValid(h5r))
			{
				fileType = CoreFileType.Geometry;
				return true;
			}
			if (BoundaryCondition.IsValid(h5r))
			{
				fileType = CoreFileType.BoundaryCondition;
				return true;
			}
			if ((bool)Terrain.IsValid(h5r))
			{
				fileType = CoreFileType.Terrain;
				return true;
			}
		}
		catch
		{
		}
		fileType = CoreFileType.Geometry;
		return false;
	}

	public void ZipFullProject(string zipFile, bool includeResults = true, ProgressReporter reporter = null)
	{
		if (IsInMemory)
		{
			throw new NotImplementedException("Cannot zip in-memory projects.");
		}
		Save();
		CompressionHelper.ZipDirectory(ProjectDirectory, zipFile, CompressionLevel.Fastest, (string s) =>
		{
			if (Path.GetFileName(s) == ".lock")
			{
				return false;
			}
			return (includeResults || !Path.GetDirectoryName(s).RoughlyStartsWith(ResultsDirectory)) ? true : false;
		}, (string s) =>
		{
			string extension = Path.GetExtension(s);
			return (!(extension == ".tif") && !(extension == ".h5")) ? true : false;
		}, reporter);
	}

	public string ExportPlanAsNewProjectZipped(Plan pl, ProjectExportConfig config = null, ProgressReporter pr = null)
	{
		if (IsInMemory)
		{
			throw new NotImplementedException("Cannot zip in-memory projects.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (config == null)
		{
			config = ProjectExportConfig.Default();
		}
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(path);
		try
		{
		OperationResult result = ExportPlanAsNewProject(pl, path, config, pr);
		if (!result) throw new InvalidOperationException(result.GetConcatenatedMessages());
		string uniqueFilename = FileHelpers.UniqueFilename(Path.Combine(EnsureDefaultExportDirectory(), pl.Name + ".export.zip"));
		CompressionHelper.ZipDirectory(path, uniqueFilename, CompressionLevel.Fastest, (string s) => !(Path.GetFileName(s) == ".lock"), (string s) =>
		{
			string extension = Path.GetExtension(s);
			return (!(extension == ".tif") && !(extension == ".h5")) ? true : false;
		}, pr);
		return uniqueFilename;
		}
		finally { Directory.Delete(path, recursive: true); }
	}

	public OperationResult ExportPlanAsNewProject(Plan pl, string directory, ProjectExportConfig config = null, ProgressReporter? pr = null)
	{
		return ExportPlansAsNewProject(new Plan[1] { pl }, directory, config, pr);
	}

	public OperationResult ExportPlansAsNewProject(IReadOnlyList<Plan> plans, string directory, ProjectExportConfig config = null, ProgressReporter pr = null)
	{
		if (IsInMemory)
		{
			throw new NotImplementedException("Cannot export in-memory projects.");
		}
		if (plans == null || plans.Count == 0)
		{
			return OperationResult.Fail("No plans were selected to export.");
		}
		if (pr == null)
		{
			pr = ProgressReporter.None();
		}
		if (config == null)
		{
			config = ProjectExportConfig.Default();
		}
		foreach (Plan plan in plans)
		{
			PlanAssociation planAssociation = GetPlanAssociation(plan);
			if (planAssociation?.Geometry == null)
			{
				return OperationResult.Fail("Cannot export plan \"" + plan.Name + "\" unless it has an attached geometry.");
			}
			if (planAssociation.BoundaryCondition == null)
			{
				return OperationResult.Fail("Cannot export plan \"" + plan.Name + "\" unless it has an attached boundary condition.");
			}
		}
		if (config.SaveSourceChanges) Save(force: false, pr);
		Project project = CreateNew(Path.Combine(directory, Name + ".ras"), ProjectionDefinition);
		try
		{
		project.Description = Description;
		Extent clipExt = Extent.Union(plans.Select((Plan p2) => GetPlanAssociation(p2).Geometry.Extent)).RatioAroundCenter(1.1);
		Dictionary<Geometry, Geometry> geometryByGeometry = new Dictionary<Geometry, Geometry>();
		Dictionary<BoundaryCondition, BoundaryCondition> boundaryConditionByBoundaryCondition = new Dictionary<BoundaryCondition, BoundaryCondition>();
		Dictionary<Terrain, Terrain> terrainMap = new Dictionary<Terrain, Terrain>();
		Dictionary<NValueLayer, NValueLayer> nvlMap = new Dictionary<NValueLayer, NValueLayer>();
		Dictionary<ClassificationLayer, ClassificationLayer> classifierMap = new Dictionary<ClassificationLayer, ClassificationLayer>();
		List<string> stringList = new List<string>();
		foreach (Plan plan2 in plans)
		{
			PlanAssociation planAssociation2 = GetPlanAssociation(plan2);
			Geometry geometry = planAssociation2.Geometry;
			BoundaryCondition boundaryCondition = planAssociation2.BoundaryCondition;
			if (!geometryByGeometry.TryGetValue(geometry, out var geometry2))
			{
				geometry2 = (geometryByGeometry[geometry] = MigrateGeometryForExport(project, geometry, clipExt, config, pr, terrainMap, nvlMap, classifierMap));
			}
			if (!boundaryConditionByBoundaryCondition.TryGetValue(boundaryCondition, out var boundaryCondition2))
			{
				boundaryCondition2 = (boundaryConditionByBoundaryCondition[boundaryCondition] = project.ImportBoundaryCondition(boundaryCondition.Filename));
				stringList.AddRange(boundaryCondition.AllExternalConnectionsNeeded());
			}
			Plan p = project.ImportPlan(plan2.Filename);
			PlanAssociation planAssociation3 = project.GetPlanAssociation(p);
			planAssociation3.Geometry = geometry2;
			planAssociation3.BoundaryCondition = boundaryCondition2;
		}
		if (stringList.Count > 0)
		{
			OperationResult migration = ProjectConnectionMigration.Migrate(this, project, stringList, config.ExternalConnectionBehavior);
			if (!migration) return migration;
		}
		project.Save();
		return true;
		}
		finally
		{
			foreach (CoreLayerBase layer in project.Plans.Cast<CoreLayerBase>().Concat(project.Geometries).Concat(project.Terrains)
				.Concat(project.BoundaryConditions).Concat(project.NValues).Concat(project.ClassificationLayers)) layer.Dispose();
		}
	}

	private Geometry MigrateGeometryForExport(Project proj, Geometry geo, Extent clipExt, ProjectExportConfig config, ProgressReporter pr, Dictionary<Terrain, Terrain> terrainMap, Dictionary<NValueLayer, NValueLayer> nvlMap, Dictionary<ClassificationLayer, ClassificationLayer> classifierMap)
	{
		GeometryAssociation geometryAssociation = GetGeometryAssociation(geo);
		Terrain terrain = geometryAssociation.Terrain;
		NValueLayer nValueLayer = geometryAssociation.NValueLayer;
		Geometry geometry = proj.ImportGeometry(geo.Filename);
		GeometryAssociation geometryAssociation2 = proj.GetGeometryAssociation(geometry);
		if (terrain != null)
		{
			if (!terrainMap.TryGetValue(terrain, out var terrainFilenameTerm))
			{
				if (config.ClipRasters || config.ResampleTerrainCellsize.HasValue)
				{
					string filename = FileHelpers.UniqueFilename(Path.Combine(proj.TerrainsDirectory, terrain.Name + ".h5"));
					Extent exportExtent = config.ClipRasters ? clipExt : terrain.ValidExtent;
					if (!exportExtent.IsValidAndFinite()) throw new ArgumentException("The export area is empty or invalid.");
					terrainFilenameTerm = terrain.ExportClipped(filename, exportExtent, config.ResampleTerrainCellsize.GetValueOrDefault(), pr);
				}
				else
				{
					terrainFilenameTerm = new Terrain(terrain.CopyAll(proj.TerrainsDirectory));
				}
				proj.AddTerrain(terrainFilenameTerm);
				terrainMap[terrain] = terrainFilenameTerm;
			}
			geometryAssociation2.Terrain = terrainFilenameTerm;
		}
		if (nValueLayer != null)
		{
			if (!nvlMap.TryGetValue(nValueLayer, out var nValueLayer2))
			{
				nValueLayer2 = (nvlMap[nValueLayer] = proj.ImportNValueLayer(nValueLayer.Filename));
				if (nValueLayer.Mode == NValueSource.ClassificationLayer)
				{
					ClassificationLayer associatedClassificationLayer = nValueLayer.AssociatedClassificationLayer;
					if (associatedClassificationLayer != null && !classifierMap.ContainsKey(associatedClassificationLayer))
					{
						ClassificationLayer classificationLayer = ((!config.ClipRasters) ? new ClassificationLayer(associatedClassificationLayer.Export(proj.ClassificationLayersDirectory)) : associatedClassificationLayer.ExportClipped(proj.ClassificationLayersDirectory, clipExt, pr));
						proj.AddClassification(classificationLayer);
						classifierMap[associatedClassificationLayer] = classificationLayer;
					}
				}
				else
				{
					_ = nValueLayer.Mode;
					_ = 2;
				}
			}
			geometryAssociation2.NValueLayer = nValueLayer2;
		}
		return geometry;
	}

	public void GetReasonableProjectTimeWindow(out DateTime start, out DateTime end)
	{
		if (BoundaryConditions.Count == 0)
		{
			start = Plan.DefaultStartTime;
			end = Plan.DefaultStartTime.AddDays(1.0);
			return;
		}
		start = DateTime.MaxValue;
		end = DateTime.MinValue;
		foreach (BoundaryCondition boundaryCondition in BoundaryConditions)
		{
			if (boundaryCondition.StartTime < start)
			{
				start = boundaryCondition.StartTime;
			}
			if (boundaryCondition.EndTime > end)
			{
				end = boundaryCondition.EndTime;
			}
		}
		if (start > end)
		{
			start = Plan.DefaultStartTime;
			end = Plan.DefaultStartTime.AddDays(1.0);
		}
	}

	public override string ToString()
	{
		return Name;
	}

	public CleanupReport ScanForCleanup(CleanupPolicy policy, bool cleanResultAuxiliary = true, bool verbose = false)
	{
		if (IsInMemory)
		{
			return new CleanupReport();
		}
		return CleanupScanner.Scan(this, policy, policy, policy, cleanResultAuxiliary, verbose);
	}

	public CleanupReport ScanForCleanup(bool verbose = false)
	{
		if (IsInMemory)
		{
			return new CleanupReport();
		}
		if (CleanupPolicy != null)
		{
			return CleanupScanner.Scan(this, CleanupPolicy, CleanupPolicy, CleanupPolicy, CleanResultAuxiliaryFiles, verbose);
		}
		return CleanupScanner.Scan(this, verbose);
	}

	public CleanupResult ExecuteCleanup(CleanupReport report, ProgressReporter pr)
	{
		if (IsInMemory)
		{
			return new CleanupResult();
		}
		return CleanupExecutor.ExecuteAll(report, pr);
	}
}
