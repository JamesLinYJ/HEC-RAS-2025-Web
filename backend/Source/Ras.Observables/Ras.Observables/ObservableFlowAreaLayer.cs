using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Ras.Hydraulics;
using Ras.Layers;
using Utility.Interfaces;
using Utility.Logging;
using Utility.Progress;
using Visual.Observables;

namespace Ras.Observables;

public class ObservableFlowAreaLayer : ObservableGeometryLayer<FlowAreaLayer, FlowArea, Observable2DFlowArea>, IReadOnlyDisplayName
{
	private BatchJob _activePropertyTablesJob;
	private BatchJob _activeComputeJob;

	public bool IsComputingPropertyTables => ActivePropertyTablesJob != null;

	public List<BatchJob> JobLog { get; } = new List<BatchJob>();

	public string DisplayName => "Mesh";

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BatchJob ActivePropertyTablesJob
	{
		get
		{
			return _activePropertyTablesJob;
		}
		[MemberNotNull("_activePropertyTablesJob")]
		set
		{
			if (!EqualityComparer<BatchJob>.Default.Equals(_activePropertyTablesJob, value))
			{
				OnPropertyChanging("ActivePropertyTablesJob");
				OnPropertyChanging("IsComputingPropertyTables");
				_activePropertyTablesJob = value;
				OnPropertyChanged("ActivePropertyTablesJob");
				OnPropertyChanged("IsComputingPropertyTables");
			}
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
				_activeComputeJob = value;
				OnPropertyChanged("ActiveComputeJob");
			}
		}
	}

	public ObservableFlowAreaLayer(ObservableGeometry geometry)
		: base(geometry, geometry.Geometry.FlowAreaLayer, (Func<FlowArea, Observable2DFlowArea>)((FlowArea ft) => new Observable2DFlowArea(ft)))
	{
	}

	public void Add(FlowArea fa)
	{
		Observable2DFlowArea observable2DFlowArea = new Observable2DFlowArea(fa);
		List<Observable2DFlowArea> newItems = new List<Observable2DFlowArea> { observable2DFlowArea };
		InvokeExplicitCollectionChanging(newItems, null);
		Geometry.Geometry.FlowAreaLayer.Add(fa);
		AlertNewFeature(observable2DFlowArea);
		InvokeExplicitCollectionChanged(newItems, null);
	}

	public OperationResult PropertyTablesAreCurrent(MeshPropertyTableSignature currentTableSig = null, ProgressReporter pr = null)
	{
		return Layer.PropertyTablesAreCurrent(currentTableSig, pr);
	}

	public async Task<OperationResult> ComputePropertyTablesAsync(BatchJob job, bool forceRecomputeTables = false)
	{
		if (IsComputingPropertyTables)
		{
			throw new Exception("Cannot be computing property tables simultaneously from different threads.");
		}
		ActivePropertyTablesJob = job;
		Geometry.NotifyCanMutateChanged();
		OperationResult result = await Task.Run(() =>
		{
			try
			{
				OperationResult operationResult = Layer.ComputePropertyTables(forceRecomputeTables, job.Reporter);
				if (!operationResult)
				{
					operationResult.AddMessage("Error computing property tables.");
				}
				return operationResult;
			}
			catch (Exception ex)
			{
				return OperationResult.Fail(GeneralMessageCodes.GetExceptionOccurred("Computing Property Tables", ex));
			}
		});
		ActivePropertyTablesJob = null;
		JobLog.Add(job);
		return result;
	}
}
