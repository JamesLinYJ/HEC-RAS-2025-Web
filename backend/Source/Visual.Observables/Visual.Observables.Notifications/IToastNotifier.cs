using System;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Visual.Observables.Notifications;

public interface IToastNotifier
{
	private static readonly ILogger _logger = LogHelper.GetLogger("IToastNotifier");

	void SendMessage(ToastMessage message);

	void SendError(string message)
	{
		SendMessage(new ToastMessage(message, MessageSeverity.Error));
	}

	void SendError(Message message)
	{
		SendMessage(new ToastMessage(message.EvaluatedMessage, MessageSeverity.Error));
	}

	void SendError(string errorDescription, OperationResult result)
	{
		string concatenatedMessages = result.GetConcatenatedMessages();
		if (string.IsNullOrEmpty(concatenatedMessages))
		{
			concatenatedMessages = "(Unknown Error)";
		}
		SendError(errorDescription + Environment.NewLine + concatenatedMessages);
	}

	void SendMessages(OperationResult opres)
	{
		foreach (Message message in opres.Messages)
		{
			if (message.MessageType.DefaultSeverity == MessageSeverity.Error)
			{
				SendError(message);
			}
			else if (message.MessageType.DefaultSeverity == MessageSeverity.Warning)
			{
				SendWarning(message.EvaluatedMessage);
			}
			else
			{
				SendInformation(message.EvaluatedMessage);
			}
		}
	}

	void SendAggregatedMessages(OperationResult opres)
	{
		foreach (AggregatedMessage enumerateAggregatedMessage in opres.EnumerateAggregatedMessages())
		{
			switch (enumerateAggregatedMessage.Message.MessageType.DefaultSeverity)
			{
			case MessageSeverity.Error:
				SendError(enumerateAggregatedMessage.FormattedMessage);
				break;
			case MessageSeverity.Warning:
				SendWarning(enumerateAggregatedMessage.FormattedMessage);
				break;
			default:
				SendInformation(enumerateAggregatedMessage.FormattedMessage);
				break;
			}
		}
	}

	void SendError(Exception ex, string message, bool log = true)
	{
		SendMessage(new ToastMessage(message + Environment.NewLine + ex.Message, MessageSeverity.Error));
		if (log)
		{
			_logger.LogError(ex, message);
		}
	}

	void SendWarning(string message)
	{
		SendMessage(new ToastMessage(message, MessageSeverity.Warning));
	}

	void SendInformation(string message)
	{
		SendMessage(new ToastMessage(message, MessageSeverity.Information));
	}
}
