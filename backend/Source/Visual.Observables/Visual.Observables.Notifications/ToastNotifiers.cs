using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Utility.Logging;

namespace Visual.Observables.Notifications;

public static class ToastNotifiers
{
	private static readonly ILogger _logger = LogHelper.GetLogger("ToastNotifiers");

	public static IToastNotifier Default { get; set; } = LogToastNotifier.Instance;

	public static void TryCatchToast(Action action, [CallerMemberName] string actionName = "")
	{
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
		}
	}

	public static void TryCatchToast<T>(Action<T> action, T model, [CallerMemberName] string actionName = "")
	{
		try
		{
			action?.Invoke(model);
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
		}
	}

	public static T TryCatchToast<T>(Func<T> action, [CallerMemberName] string actionName = "")
	{
		try
		{
			if (action == null)
			{
				return default;
			}
			return action();
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
			return default;
		}
	}

	public static T TryCatchToast<T, K>(Func<K, T> action, K model, [CallerMemberName] string actionName = "")
	{
		try
		{
			if (action == null)
			{
				return default;
			}
			return action(model);
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
			return default;
		}
	}

	public static async Task TryCatchToastAsync(Func<Task> action, [CallerMemberName] string actionName = "")
	{
		try
		{
			if (action != null)
			{
				await action();
			}
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
		}
	}

	public static async Task TryCatchToastAsync<T>(Func<T, Task> action, T model, [CallerMemberName] string actionName = "")
	{
		try
		{
			if (action != null)
			{
				await action(model);
			}
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
		}
	}

	public static async Task<T> TryCatchToastAsync<T>(Func<Task<T>> action, [CallerMemberName] string actionName = "")
	{
		try
		{
			if (action == null)
			{
				return default;
			}
			return await action();
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
		}
		return default;
	}

	public static async Task<T> TryCatchToastAsync<T, K>(Func<K, Task<T>> action, K model, [CallerMemberName] string actionName = "")
	{
		try
		{
			if (action == null)
			{
				return default;
			}
			return await action(model);
		}
		catch (Exception ex)
		{
			try
			{
				if (string.IsNullOrEmpty(actionName))
				{
					Default?.SendError(ex, "Could not complete action.", log: false);
				}
				else
				{
					Default?.SendError(ex, "Could not complete '" + actionName + "'.", log: false);
				}
				LogActionError(ex, actionName);
			}
			catch
			{
			}
		}
		return default;
	}

	private static void LogActionError(Exception ex, string actionName)
	{
		_logger.LogError(ex, "Error running action \"{ActionName}\"", actionName);
	}
}
