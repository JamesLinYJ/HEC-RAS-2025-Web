using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Utility;
using Utility.DataStructures;
using Utility.Logging;

namespace Visual.Observables.Notifications;

public class ObservableToastNotifier : ObservableObject, IToastNotifier
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ObservableToastNotifier");

	private int _recentMessageTimeoutMs;

	private double _recentMessageTimeoutRatio = 100.0;

	private double _recentMessageTImeoutMinRatio = 0.25;

	private ISynchronizationContext _uiThreadSyncContext;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<ToastMessage> Messages
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<ToastMessage>>.Default.Equals(field, value))
			{
				OnPropertyChanging("Messages");
				field = value;
				OnPropertyChanged("Messages");
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public AdvancedCollection<ToastMessage> RecentMessages
	{
		get
		{
			return field;
		}
		set
		{
			if (!EqualityComparer<AdvancedCollection<ToastMessage>>.Default.Equals(field, value))
			{
				OnPropertyChanging("RecentMessages");
				field = value;
				OnPropertyChanged("RecentMessages");
			}
		}
	}

	public bool IsReversed { get; set; }

	public ObservableToastNotifier(ISynchronizationContext uiThreadSyncContext, int recentMessageTimeoutMs = 5000)
	{
		_recentMessageTimeoutMs = recentMessageTimeoutMs;
		_uiThreadSyncContext = uiThreadSyncContext;
	}

	public void SendMessage(ToastMessage message)
	{
		if (message.Severity == MessageSeverity.Error)
		{
			_logger.LogError("Toast: {msg}", message.Message);
		}
		else if (message.Severity == MessageSeverity.Warning)
		{
			_logger.LogWarning("Toast: {msg}", message.Message);
		}
		_uiThreadSyncContext.Sync(async () =>
		{
			if (Messages == null)
			{
				Messages = new AdvancedCollection<ToastMessage>();
				RecentMessages = new AdvancedCollection<ToastMessage>();
			}
			if (IsReversed)
			{
				Messages.Insert(0, message);
				RecentMessages.Insert(0, message);
			}
			else
			{
				Messages.Add(message);
				RecentMessages.Add(message);
			}
			double lengthOverRecentMessageTimeoutRatio = (double)message.Message.Length / _recentMessageTimeoutRatio;
			lengthOverRecentMessageTimeoutRatio = Math.Min(lengthOverRecentMessageTimeoutRatio, 1.0);
			lengthOverRecentMessageTimeoutRatio = Math.Max(lengthOverRecentMessageTimeoutRatio, _recentMessageTImeoutMinRatio);
			await Task.Delay((int)((double)_recentMessageTimeoutMs * lengthOverRecentMessageTimeoutRatio));
			RecentMessages.Remove(message);
		});
	}
}
