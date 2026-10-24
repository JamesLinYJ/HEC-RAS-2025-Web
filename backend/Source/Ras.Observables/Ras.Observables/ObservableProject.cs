using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.GDALAssist;
using Microsoft.Extensions.Logging;
using Ras.Layers;
using Utility;
using Utility.DataStructures;
using Utility.Extensions;
using Utility.Logging;
using Utility.Units;
using Visual.Observables;

namespace Ras.Observables;

public class ObservableProject : ObservableObject
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableProject");

	private readonly Project _prj;

	private readonly AdvancedCollection<ObservableGeometry> _geometries;

	private readonly AdvancedCollection<ObservableBoundaryCondition> _boundaryConditions;

	private readonly AdvancedCollection<ObservablePlan> _plans;

	private AdvancedCollection<ObservableResult> _results;

	private readonly AdvancedCollection<ObservableTerrain> _terrains;

	private readonly AdvancedCollection<ObservableClassificationLayer> _classifications;

	private readonly AdvancedCollection<ObservableNValueLayer> _nValues;

	public Project Project => _prj;

	public IReadOnlyAdvancedCollection<ObservableGeometry> Geometries => _geometries;

	public IReadOnlyAdvancedCollection<ObservableBoundaryCondition> BoundaryConditions => _boundaryConditions;

	public IReadOnlyAdvancedCollection<ObservablePlan> Plans => _plans;

	public IReadOnlyAdvancedCollection<ObservableResult> Results => _results;

	public IReadOnlyAdvancedCollection<ObservableTerrain> Terrains => _terrains;

	public IReadOnlyAdvancedCollection<ObservableClassificationLayer> Classifications => _classifications;

	public IReadOnlyAdvancedCollection<ObservableNValueLayer> NValues => _nValues;

	public ObservableExternalConnectionCollection Connections { get; }

	public ObservableHydraulicDesignData HydraulicDesign { get; }

	public string Filename => Project.Filename;

	public string Name => Project.Name;

	public Projection Projection => Project.Projection;

	public UnitSystem Units => Project.Units;

	public string Description
	{
		get
		{
			return Project.Description;
		}
		set
		{
			SetProperty(Project.Description, value, Project, (Project p, string v) =>
			{
				p.Description = v;
			}, "Description");
		}
	}

	public bool HasProjection => Projection != null;

	public ISynchronizationContext SynchronizationContext { get; private set; }

	public ObservableProject(Project prj, ISynchronizationContext syncContext)
	{
		if (prj != null)
		{
			_prj = prj;
			SynchronizationContext = syncContext;
			_geometries = new AdvancedCollection<ObservableGeometry>(_prj.Geometries.Select((Geometry g) => new ObservableGeometry(this, g)).ToList());
			_boundaryConditions = new AdvancedCollection<ObservableBoundaryCondition>(_prj.BoundaryConditions.Select((BoundaryCondition b) => new ObservableBoundaryCondition(this, b)).ToList());
			_plans = new AdvancedCollection<ObservablePlan>(_prj.Plans.Select((Plan p) => new ObservablePlan(this, p)).ToList());
			InitializeResults();
			_terrains = new AdvancedCollection<ObservableTerrain>(_prj.Terrains.Select((Terrain t) => new ObservableTerrain(this, t)).ToList());
			_classifications = new AdvancedCollection<ObservableClassificationLayer>(_prj.ClassificationLayers.Select((ClassificationLayer c) => new ObservableClassificationLayer(this, c)).ToList());
			_nValues = new AdvancedCollection<ObservableNValueLayer>(_prj.NValues.Select((NValueLayer c) => new ObservableNValueLayer(this, c)).ToList());
			Connections = new ObservableExternalConnectionCollection(_prj.ConnectionSource);
			HydraulicDesign = new ObservableHydraulicDesignData(_prj.HydraulicDesign);
		}
	}

	public bool TryAddGeometry(ObservableGeometry geom)
	{
		_geometries.Add(geom);
		_prj.AddGeometry(geom.Geometry);
		return true;
	}

	public ObservableGeometry AddNewGeometry(string geomName)
	{
		Geometry geom = _prj.AddNewGeometry(geomName);
		ObservableGeometry observableGeometry = new ObservableGeometry(this, geom);
		_geometries.Add(observableGeometry);
		return observableGeometry;
	}

	public ObservableGeometry TryAddExistingGeometry(string geomFilename)
	{
		if (_geometries.Any((ObservableGeometry g) => g.Filename.RoughlyEquals(geomFilename)))
		{
			_logger.LogInformation("Could not add existing geometry {GeometryFilename} (Already present)", geomFilename);
			return null;
		}
		Geometry geom = new Geometry(geomFilename);
		_prj.AddGeometry(geom);
		ObservableGeometry observableGeometry = new ObservableGeometry(this, geom);
		_geometries.Add(observableGeometry);
		return observableGeometry;
	}

	public void RemoveGeometry(ObservableGeometry geom)
	{
		_geometries.Remove(geom);
		Project.RemoveGeometry(geom.Geometry);
	}

	public bool TryRemoveGeometryByFilename(string fn)
	{
		Geometry g = Project.TryRemoveGeometryByFilename(fn);
		if (g != null)
		{
			ObservableGeometry observableGeometry = _geometries.FirstOrDefault((ObservableGeometry observableGeometry2) => observableGeometry2.Geometry == g);
			if (observableGeometry != null)
			{
				_geometries.Remove(observableGeometry);
				return true;
			}
			_logger.LogWarning("Geometry {Filename} removed from model, but not present in Observable layer.", fn);
			return false;
		}
		_logger.LogInformation("Could not remove Geometry {Filename} from model, item not present.", fn);
		return false;
	}

	public ObservableGeometry TryFindGeometryByFilename(string fn)
	{
		Geometry g = Project.TryFindGeometryByFilename(fn);
		if (g == null)
		{
			return null;
		}
		return _geometries.FirstOrDefault((ObservableGeometry obs) => obs.Geometry == g);
	}

	public void AddBoundaryCondition(ObservableBoundaryCondition bc)
	{
		_boundaryConditions.Add(bc);
		_prj.AddBoundaryCondition(bc.BoundaryCondition);
	}

	public ObservableBoundaryCondition AddNewBoundaryCondition(string bcName)
	{
		BoundaryCondition bc = _prj.AddNewBoundaryCondition(bcName);
		ObservableBoundaryCondition observableBoundaryCondition = new ObservableBoundaryCondition(this, bc);
		_boundaryConditions.Add(observableBoundaryCondition);
		return observableBoundaryCondition;
	}

	public ObservableBoundaryCondition TryAddExistingBoundaryCondition(string bcFilename)
	{
		if (_boundaryConditions.Any((ObservableBoundaryCondition b) => b.Filename.RoughlyEquals(bcFilename)))
		{
			return null;
		}
		BoundaryCondition bc = new BoundaryCondition(bcFilename);
		_prj.AddBoundaryCondition(bc);
		ObservableBoundaryCondition observableBoundaryCondition = new ObservableBoundaryCondition(this, bc);
		_boundaryConditions.Add(observableBoundaryCondition);
		return observableBoundaryCondition;
	}

	public void RemoveBoundaryCondition(ObservableBoundaryCondition bc)
	{
		_boundaryConditions.Remove(bc);
		Project.RemoveBoundaryCondition(bc.Layer);
	}

	public bool TryRemoveBoundaryConditionByFilename(string fn)
	{
		BoundaryCondition b = Project.TryRemoveBoundaryConditionByFilename(fn);
		if (b != null)
		{
			ObservableBoundaryCondition observableBoundaryCondition = _boundaryConditions.FirstOrDefault((ObservableBoundaryCondition observableBoundaryCondition2) => observableBoundaryCondition2.BoundaryCondition == b);
			if (observableBoundaryCondition != null)
			{
				_boundaryConditions.Remove(observableBoundaryCondition);
				return true;
			}
			_logger.LogWarning("Boundary Condition {Filename} removed from model, but not present in Observable layer.", fn);
			return false;
		}
		_logger.LogInformation("Could not remove Boundary Condition {Filename} from model, item not present.", fn);
		return false;
	}

	public ObservableBoundaryCondition TryFindBoundaryConditionByFilename(string fn)
	{
		BoundaryCondition bc = Project.TryFindBoundaryConditionByFilename(fn);
		if (bc == null)
		{
			return null;
		}
		return _boundaryConditions.FirstOrDefault((ObservableBoundaryCondition obs) => obs.BoundaryCondition == bc);
	}

	public ObservablePlan AddPlan(ObservablePlan pl)
	{
		_plans.Add(pl);
		_prj.AddPlan(pl.Plan);
		return pl;
	}

	public ObservablePlan AddNewPlan(string plName)
	{
		Plan plan = _prj.AddNewPlan(plName);
		ObservablePlan observablePlan = new ObservablePlan(this, plan);
		_plans.Add(observablePlan);
		return observablePlan;
	}

	public ObservablePlan TryAddExistingPlan(string plFilename)
	{
		if (_plans.Any((ObservablePlan b) => b.Filename.RoughlyEquals(plFilename)))
		{
			return null;
		}
		Plan plan = new Plan(plFilename);
		_prj.AddPlan(plan);
		ObservablePlan observablePlan = new ObservablePlan(this, plan);
		_plans.Add(observablePlan);
		return observablePlan;
	}

	public ObservablePlan TryFindPlanByFilename(string fn)
	{
		Plan bc = Project.TryFindPlanByFilename(fn);
		if (bc == null)
		{
			return null;
		}
		return _plans.FirstOrDefault((ObservablePlan obs) => obs.Plan == bc);
	}

	public bool TryRemovePlanByFilename(string fn)
	{
		Plan b = Project.TryRemovePlanByFilename(fn);
		if (b != null)
		{
			ObservablePlan observablePlan = _plans.FirstOrDefault((ObservablePlan observablePlan2) => observablePlan2.Plan == b);
			if (observablePlan != null)
			{
				_plans.Remove(observablePlan);
				return true;
			}
			_logger.LogWarning("Plan {Filename} removed from model, but not present in Observable layer.", fn);
			return false;
		}
		_logger.LogInformation("Could not remove Plan {Filename} from model, item not present.", fn);
		return false;
	}

	public void RemovePlan(ObservablePlan p)
	{
		_plans.Remove(p);
		Project.RemovePlan(p.Layer);
	}

	private void InitializeResults()
	{
		IReadOnlyDictionary<string, ObservablePlan> planNameLookup = GetPlanNameLookup();
		AdvancedCollection<ObservableResult> advancedCollection = new AdvancedCollection<ObservableResult>();
		foreach (Result result in _prj.Results)
		{
			ObservableResult observableResult = new ObservableResult(this, result);
			OnResultAdded(observableResult, planNameLookup);
			advancedCollection.Add(observableResult);
		}
		_results = advancedCollection;
	}

	public ObservableResult TryFindObservableResult(Result res)
	{
		return _results.FirstOrDefault((ObservableResult or) => or.Result == res);
	}

	public void RemoveResult(ObservableResult res)
	{
		_results.Remove(res);
		Project.RemoveResult(res.Result);
	}

	public void AddResult(ObservableResult res)
	{
		if (!Project.Results.Contains(res.Result))
		{
			Project.AddResult(res.Result);
		}
		_results.Add(res);
		OnResultAdded(res);
	}

	private void OnResultAdded(ObservableResult res, IReadOnlyDictionary<string, ObservablePlan> planNameLookup = null)
	{
		if (planNameLookup == null)
		{
			planNameLookup = GetPlanNameLookup();
		}
		if (planNameLookup.TryGetValue(res.Plan.Name, out var localValue))
		{
			localValue.NameMatchedResults.Add(res);
		}
	}

	private IReadOnlyDictionary<string, ObservablePlan> GetPlanNameLookup()
	{
		Dictionary<string, ObservablePlan> planNameLookup = new Dictionary<string, ObservablePlan>();
		foreach (ObservablePlan plan in _plans)
		{
			planNameLookup[plan.Name] = plan;
		}
		return planNameLookup;
	}

	public ObservableTerrain TryAddExistingTerrain(string trFilename)
	{
		if (_terrains.Any((ObservableTerrain b) => b.Terrain.Filename.RoughlyEquals(trFilename)))
		{
			return null;
		}
		Terrain terrain = null;
		terrain = ((!(Path.GetExtension(trFilename) == ".s3")) ? new Terrain(trFilename) : Terrain.FromS3(trFilename));
		_prj.AddTerrain(terrain);
		ObservableTerrain observableTerrain = new ObservableTerrain(this, terrain);
		_terrains.Add(observableTerrain);
		return observableTerrain;
	}

	public ObservableTerrain AddNewTerrain(string trName)
	{
		Terrain terr = _prj.AddNewTerrain(trName);
		ObservableTerrain observableTerrain = new ObservableTerrain(this, terr);
		_terrains.Add(observableTerrain);
		return observableTerrain;
	}

	public ObservableTerrain AddTerrain(Terrain tr)
	{
		ObservableTerrain observableTerrain = new ObservableTerrain(this, tr);
		_terrains.Add(observableTerrain);
		_prj.AddTerrain(tr);
		return observableTerrain;
	}

	public void AddTerrain(ObservableTerrain tr)
	{
		_terrains.Add(tr);
		_prj.AddTerrain(tr.Terrain);
	}

	public void RemoveTerrain(ObservableTerrain tr)
	{
		_terrains.Remove(tr);
		_prj.RemoveTerrain(tr.Terrain);
	}

	public bool TryAddClassificationLayer(ObservableClassificationLayer lyr)
	{
		_classifications.Add(lyr);
		_prj.AddClassification(lyr.ClassificationLayer);
		return true;
	}

	public ObservableClassificationLayer AddNewClassification(string name)
	{
		ClassificationLayer lyr = _prj.AddNewClassification(name);
		ObservableClassificationLayer observableClassificationLayer = new ObservableClassificationLayer(this, lyr);
		_classifications.Add(observableClassificationLayer);
		return observableClassificationLayer;
	}

	public ObservableClassificationLayer TryAddNewClassification(string name)
	{
		ClassificationLayer lyr = _prj.TryAddNewClassification(name);
		ObservableClassificationLayer observableClassificationLayer = new ObservableClassificationLayer(this, lyr);
		_classifications.Add(observableClassificationLayer);
		return observableClassificationLayer;
	}

	public ObservableClassificationLayer TryAddExistingClassification(string filename)
	{
		if (_classifications.Any((ObservableClassificationLayer g) => g.Filename.RoughlyEquals(filename)))
		{
			_logger.LogInformation("Could not add existing classification {Filename} (Already present)", filename);
			return null;
		}
		ClassificationLayer lyr = new ClassificationLayer(filename);
		_prj.AddClassification(lyr);
		ObservableClassificationLayer observableClassificationLayer = new ObservableClassificationLayer(this, lyr);
		_classifications.Add(observableClassificationLayer);
		return observableClassificationLayer;
	}

	public void RemoveClassification(ObservableClassificationLayer lyr)
	{
		_classifications.Remove(lyr);
		Project.RemoveClassification(lyr.ClassificationLayer);
	}

	public bool TryRemoveClassificationByFilename(string fn)
	{
		ClassificationLayer g = Project.TryRemoveClassificationByFilename(fn);
		if (g != null)
		{
			ObservableClassificationLayer observableClassificationLayer = _classifications.FirstOrDefault((ObservableClassificationLayer observableClassificationLayer2) => observableClassificationLayer2.ClassificationLayer == g);
			if (observableClassificationLayer != null)
			{
				_classifications.Remove(observableClassificationLayer);
				return true;
			}
			_logger.LogWarning("Classification {Filename} removed from model, but not present in Observable layer.", fn);
			return false;
		}
		_logger.LogInformation("Could not remove Classification {Filename} from model, item not present.", fn);
		return false;
	}

	public ObservableClassificationLayer TryFindClassificationByFilename(string fn)
	{
		ClassificationLayer g = Project.TryFindClassificationByFilename(fn);
		if (g == null)
		{
			return null;
		}
		return _classifications.FirstOrDefault((ObservableClassificationLayer obs) => obs.ClassificationLayer == g);
	}

	public ObservableNValueLayer AddNewNValue(string name)
	{
		NValueLayer lyr = _prj.AddNewNValueLayer(name);
		ObservableNValueLayer observableNValueLayer = new ObservableNValueLayer(this, lyr);
		_nValues.Add(observableNValueLayer);
		return observableNValueLayer;
	}

	public ObservableNValueLayer TryAddNewNValue(string name)
	{
		NValueLayer lyr = _prj.TryAddNewNValueLayer(name);
		ObservableNValueLayer observableNValueLayer = new ObservableNValueLayer(this, lyr);
		_nValues.Add(observableNValueLayer);
		return observableNValueLayer;
	}

	public ObservableNValueLayer TryAddExistingNValueLayer(string filename)
	{
		if (_nValues.Any((ObservableNValueLayer g) => g.Filename.RoughlyEquals(filename)))
		{
			_logger.LogInformation("Could not add existing nval {Filename} (Already present)", filename);
			return null;
		}
		NValueLayer lyr = new NValueLayer(filename);
		_prj.AddNValueLayer(lyr);
		ObservableNValueLayer observableNValueLayer = new ObservableNValueLayer(this, lyr);
		_nValues.Add(observableNValueLayer);
		return observableNValueLayer;
	}

	public void RemoveNValue(ObservableNValueLayer lyr)
	{
		_nValues.Remove(lyr);
		Project.RemoveNValue(lyr.NVLayer);
	}

	public bool TryRemoveNValueByFilename(string fn)
	{
		NValueLayer g = Project.TryRemoveNValueByFilename(fn);
		if (g != null)
		{
			ObservableNValueLayer observableNValueLayer = _nValues.FirstOrDefault((ObservableNValueLayer observableNValueLayer2) => observableNValueLayer2.NVLayer == g);
			if (observableNValueLayer != null)
			{
				_nValues.Remove(observableNValueLayer);
				return true;
			}
			_logger.LogWarning("NVLayer {Filename} removed from model, but not present in Observable layer.", fn);
			return false;
		}
		_logger.LogInformation("Could not remove NVLayer {Filename} from model, item not present.", fn);
		return false;
	}

	public ObservableNValueLayer TryFindNValueByFilename(string fn)
	{
		NValueLayer g = Project.TryFindNValueByFilename(fn);
		if (g == null)
		{
			return null;
		}
		return _nValues.FirstOrDefault((ObservableNValueLayer obs) => obs.NVLayer == g);
	}

	public ObservableTerrain TryFindTerrainByFilename(string fn)
	{
		Terrain g = Project.TryFindTerrainByFilename(fn);
		if (g == null)
		{
			return null;
		}
		return _terrains.FirstOrDefault((ObservableTerrain obs) => obs.Terrain == g);
	}

	public void AlertFileRename(string oldFilename, string newFilename)
	{
		ObservableGeometry observableGeometry = TryFindGeometryByFilename(oldFilename);
		if (observableGeometry != null)
		{
			observableGeometry.AlertFileRename_CrossThread(newFilename);
			return;
		}
		ObservableBoundaryCondition observableBoundaryCondition = TryFindBoundaryConditionByFilename(oldFilename);
		if (observableBoundaryCondition != null)
		{
			observableBoundaryCondition.AlertFileRename_CrossThread(newFilename);
		}
		else
		{
			TryFindPlanByFilename(oldFilename)?.AlertFileRename_CrossThread(newFilename);
		}
	}
}
