using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Geospatial.Vectors.Rasterizers;
using Ras.Layers;
using Ras.Layers.Results;
using Ras.Mapping;
using Ras.Rendering;
using Utility.Interfaces;
using Utility.Logging;
using Visual.Observables;
using Visual.Observables.Interfaces;

namespace Ras.Observables;

public class ObservableResult : ObservableBaseLayer, IObservableReadOnlyDisplayName, IReadOnlyDisplayName
{
	private readonly Result _res;

	private ObservableGeometry _geometry;

	private ObservableBoundaryCondition _boundaryCondition;

	private ObservablePlan _plan;
	private BatchJob _activeComputeJob;

	public static Type Type => typeof(ObservableResult);

	public Result Result => _res;

	public string Name
	{
		get
		{
			return _res.Name;
		}
		set
		{
			if (SetProperty(_res.Name, value, _res, (Result fa, string v) =>
			{
				fa.Name = v;
			}, "Name"))
			{
				OnPropertyChanged("DisplayName");
				NameChanged?.Invoke(this, value);
			}
		}
	}

	public string FriendlyFailureReason
	{
		get
		{
			ComputeSummary computeSummary = _res.ComputeSummary;
			if (computeSummary == null)
			{
				return "Unknown Compute Error";
			}
			if (computeSummary.Success)
			{
				return "";
			}
			if (string.IsNullOrEmpty(computeSummary.FailureReason))
			{
				return "Compute failed at some point through the simulation.";
			}
			return "Compute failed at some point through the simulation: " + computeSummary.FailureReason;
		}
	}

	public ObservableGeometry Geometry
	{
		get
		{
			return _geometry;
		}
		private init
		{
			SetProperty(ref _geometry, value, "Geometry");
		}
	}

	public ObservableBoundaryCondition BoundaryCondition
	{
		get
		{
			return _boundaryCondition;
		}
		private init
		{
			SetProperty(ref _boundaryCondition, value, "BoundaryCondition");
		}
	}

	public ObservablePlan Plan
	{
		get
		{
			if (_plan == null)
			{
				_plan = new ObservablePlan(Project, Result.Plan);
			}
			return _plan;
		}
	}

	public string DisplayName => Name;

	public bool IsComputing => ActiveComputeJob != null;

	public bool ComputedSuccessfully => _res.ComputeSummary?.Success ?? false;

	public bool ComputedSuccessfullyOrIsComputing
	{
		get
		{
			if (!ComputedSuccessfully)
			{
				return IsComputing;
			}
			return true;
		}
	}

	public override string Filename => Result.Filename;

	public override bool IsInMemory => Result.IsInMemory;

	public IReadOnlyList<Message> LayerErrors => Result.LayerErrors;

	public bool HasLoadErrors => Result.HasLoadErrors;

	public ObservableTerrain AssociatedTerrain
	{
		get
		{
			Terrain actualAssn = Result.AssociatedTerrain;
			if (actualAssn == null)
			{
				return null;
			}
			return Project.Terrains.FirstOrDefault((ObservableTerrain obsTerr) => obsTerr.Terrain.Equals(actualAssn));
		}
	}

	public ObservableTerrain OverrideTerrain
	{
		get
		{
			ResultAssociation assn = Result.Project.GetResultAssociation(Result);
			if (!assn.HasOverride)
			{
				return null;
			}
			return Project.Terrains.FirstOrDefault((ObservableTerrain obsTerr) => obsTerr.Terrain.Equals(assn.OverrideTerrain));
		}
		set
		{
			if (value == null)
			{
				Result.Project.ClearResultTerrainOverride(Result);
			}
			else
			{
				Result.Project.SetResultTerrainOverride(Result, value.Terrain);
			}
			OnPropertyChanged("OverrideTerrain");
			OnPropertyChanged("AssociatedTerrain");
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BatchJob ActiveComputeJob
	{
		get
		{
			return _activeComputeJob;
		}
		[MemberNotNull("_activeComputeJob")]
		set
		{
			if (!EqualityComparer<BatchJob>.Default.Equals(_activeComputeJob, value))
			{
				OnPropertyChanging("ActiveComputeJob");
				OnPropertyChanging("ComputedSuccessfully");
				OnPropertyChanging("IsComputing");
				OnPropertyChanging("ComputedSuccessfullyOrIsComputing");
				_activeComputeJob = value;
				OnPropertyChanged("ActiveComputeJob");
				OnPropertyChanged("ComputedSuccessfully");
				OnPropertyChanged("IsComputing");
				OnPropertyChanged("ComputedSuccessfullyOrIsComputing");
			}
		}
	}

	public event IObservableReadOnlyDisplayName.NameChangedEvent NameChanged;

	public ObservableResult(ObservableProject prj, Result res)
		: base(prj, res)
	{
		_res = res;
		Geometry = new ObservableGeometry(this);
		BoundaryCondition = new ObservableBoundaryCondition(this);
		if (Result.Geometry != null)
		{
			Result.Geometry.ErrorsChanged += Geometry_ErrorsChanged;
		}
	}

	private void Geometry_ErrorsChanged(object sender, EventArgs e)
	{
		Project.SynchronizationContext.Sync(() =>
		{
			OnPropertyChanged("LayerErrors");
			OnPropertyChanged("HasLoadErrors");
		});
	}

	public ObservableResultMap CreateMap(MapTypes mt)
	{
		return new ObservableResultMap(this, new ResultMap(_res, mt, _res.AssociatedTerrain));
	}

	public ObservableResultMap CreateMap(GenericMapType mt)
	{
		return new ObservableResultMap(this, new ResultMap(_res, mt, _res.AssociatedTerrain));
	}

	protected override void DeleteSelfInternal()
	{
		Project.RemoveResult(this);
		Result.DisposeAndDeleteAllBackingFiles();
	}
}
