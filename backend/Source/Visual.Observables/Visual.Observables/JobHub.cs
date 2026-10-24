using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Utility;

namespace Visual.Observables;

public class JobHub : ObservableObject
{
	private ISynchronizationContext _defaultUIThreadSyncContext;

	public static JobHub Instance { get; private set; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ObservableCollection<BatchJob> Jobs
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<ObservableCollection<BatchJob>>.Default.Equals(field, value))
			{
				OnPropertyChanging("Jobs");
				field = value;
				OnPropertyChanged("Jobs");
			}
		}
	} = new ObservableCollection<BatchJob>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasJobs
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("HasJobs");
				field = value;
				OnPropertyChanged("HasJobs");
			}
		}
	}

	public static void Initialize(ISynchronizationContext uiThreadSyncContext)
	{
		if (Instance != null)
		{
			throw new InvalidOperationException("JobHub already initialized.");
		}
		Instance = new JobHub(uiThreadSyncContext);
	}

	private JobHub(ISynchronizationContext uiThreadSyncContext)
	{
		_defaultUIThreadSyncContext = uiThreadSyncContext;
		Jobs.CollectionChanged += Jobs_CollectionChanged;
	}

	public BatchJob CreateJob(ISynchronizationContext uiThreadSyncContext, string name = null)
	{
		BatchJob batchJob = new BatchJob(name, this)
		{
			UIThreadSyncContext = uiThreadSyncContext
		};
		Jobs.Insert(0, batchJob);
		return batchJob;
	}

	public BatchJob CreateJob(string name = null)
	{
		return CreateJob(_defaultUIThreadSyncContext, name);
	}

	private void Jobs_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
	{
		HasJobs = Jobs.Count > 0;
	}

	public void CancelAndClearAllJobs()
	{
		foreach (BatchJob job in Jobs)
		{
			if (job.IsRunning)
			{
				job.RequestCancel();
			}
		}
		Jobs.Clear();
	}
}
