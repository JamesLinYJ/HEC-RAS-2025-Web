using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Utility;
using Utility.DataStructures;
using Utility.Helpers;
using Utility.Logging;
using Utility.Progress;

namespace Visual.Observables;

public class BatchJob : ObservableObject
{
	public delegate void CompletedEvent(BatchJob sender);

	public delegate void ProgressChangedEvent(BatchJob sender, double progress);

	public static readonly BatchJob CompletedJob = new BatchJob("Completed job")
	{
		IsComplete = true,
		TaskSucceeded = true,
		TimeTaken = new TimeSpan(0L),
		Message = "Already completed job",
		Progress = 100.0
	};

	private DateTime _dtStart;

	private DateTime _dtEnd;

	public ISynchronizationContext UIThreadSyncContext = SynchronizationContext.None;

	private readonly JobHub _parent;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? deleteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? requestCancelCommand;

	public DateTime StartTime => _dtStart;

	public BatchJobCleanupOptions CleanupOptions { get; set; } = BatchJobCleanupOptions.Never;

	public ProgressReporter Reporter { get; set; }

	public AdvancedCollection<Message> Messages { get; } = new AdvancedCollection<Message>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Name
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("Name");
				field = value;
				OnPropertyChanged("Name");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double Progress
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(field, value))
			{
				OnPropertyChanging("Progress");
				field = value;
				OnProgressChanged(value);
				OnPropertyChanged("Progress");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Message
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(field, value))
			{
				OnPropertyChanging("Message");
				field = value;
				OnPropertyChanged("Message");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsComplete
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				bool oldValue = field;
				OnPropertyChanging("IsComplete");
				OnPropertyChanging("IsRunning");
				field = value;
				OnIsCompleteChanged(oldValue, value);
				OnPropertyChanged("IsComplete");
				OnPropertyChanged("IsRunning");
			}
		}
	}

	public bool IsRunning => !IsComplete;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool TaskSucceeded
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("TaskSucceeded");
				field = value;
				OnPropertyChanged("TaskSucceeded");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsCancelling
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(field, value))
			{
				OnPropertyChanging("IsCancelling");
				field = value;
				OnPropertyChanged("IsCancelling");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public TimeSpan? TimeTaken
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<TimeSpan?>.Default.Equals(field, value))
			{
				OnPropertyChanging("TimeTaken");
				field = value;
				OnPropertyChanged("TimeTaken");
			}
		}
	}

	public bool CanDelete => _parent != null;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DeleteCommand => deleteCommand ?? (deleteCommand = new RelayCommand(Delete));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RequestCancelCommand => requestCancelCommand ?? (requestCancelCommand = new RelayCommand(RequestCancel));

	public event CompletedEvent Completed;

	public event ProgressChangedEvent ProgressChanged;

	public WeakHandler SubscribeToProgressChanged<TModel>(TModel model, Action<TModel, BatchJob, double> progressChangedCallback) where TModel : class
	{
		return WeakHandler.TrySubscribe(this, "ProgressChanged", model, progressChangedCallback);
	}

	public WeakHandler SubscribeToCompleted<TModel>(TModel model, Action<TModel, BatchJob> completedCallback) where TModel : class
	{
		return WeakHandler.TrySubscribe(this, "Completed", model, completedCallback);
	}

	public BatchJob(string name = null, JobHub parent = null, ISynchronizationContext uiThreadSyncContext = null)
		: this(new ProgressReporter(), name, parent, uiThreadSyncContext)
	{
	}

	public BatchJob(ProgressReporter pr, string name = null, JobHub parent = null, ISynchronizationContext uiThreadSyncContext = null)
	{
		if (pr == null)
		{
			throw new ArgumentNullException();
		}
		_parent = parent;
		Name = name;
		Reporter = pr;
		pr.MessageReported += Prog_MessageReported;
		pr.ProgressReported += Prog_ProgressReported;
		_dtStart = DateTime.Now;
		if (uiThreadSyncContext != null)
		{
			UIThreadSyncContext = uiThreadSyncContext;
		}
	}

	private void Prog_ProgressReported(ProgressReporter sender, int prog)
	{
		UIThreadSyncContext.Sync(() =>
		{
			Progress = prog;
		});
	}

	public void MarkComplete(bool success)
	{
		UIThreadSyncContext.Sync(() =>
		{
			TaskSucceeded = success;
			if (success)
			{
				Reporter.ReportProgress(100);
			}
			Reporter.ProgressReported -= Prog_ProgressReported;
			Reporter.MessageReported -= Prog_MessageReported;
			_dtEnd = DateTime.Now;
			TimeTaken = _dtEnd - _dtStart;
			IsComplete = true;
		});
	}

	private void Prog_MessageReported(ProgressReporter sender, Message msg)
	{
		UIThreadSyncContext.Sync(() =>
		{
			Messages.Add(msg);
			Message = msg.EvaluatedMessage;
		});
	}

	public void Delete()
	{
		Reporter.RequestCancel();
		_parent.Jobs.Remove(this);
	}

	public void RequestCancel()
	{
		IsCancelling = true;
		Reporter.RequestCancel();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnProgressChanged(double computed)
	{
		ProgressChanged?.Invoke(this, computed);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsCompleteChanged(bool oldValue, bool newValue)
	{
		if (newValue)
		{
			Completed?.Invoke(this);
		}
	}
}
