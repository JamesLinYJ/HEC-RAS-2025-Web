using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Visual.Observables.Notifications;

internal class LogToastNotifier : IToastNotifier
{
	private static readonly ILogger _logger = LogHelper.GetLogger("LogToastNotifier");

	public static readonly LogToastNotifier Instance = new LogToastNotifier();

	public void SendMessage(ToastMessage toast)
	{
		if (toast.Severity == MessageSeverity.Error)
		{
			_logger.LogError("Toast: {Message}", toast.Message);
		}
		else if (toast.Severity == MessageSeverity.Warning)
		{
			_logger.LogWarning("Toast: {Message}", toast.Message);
		}
		else
		{
			_logger.LogInformation("Toast: {Message}", toast.Message);
		}
	}
}
