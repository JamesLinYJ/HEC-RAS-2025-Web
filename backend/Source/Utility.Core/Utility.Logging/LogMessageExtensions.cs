using Microsoft.Extensions.Logging;

namespace Utility.Logging;

public static class LogMessageExtensions
{
	public static void LogWarning(this ILogger logger, Message msg)
	{
		logger.LogWarning(msg.MessageType.DefaultFormattedMessage, msg.LogData);
	}
}
