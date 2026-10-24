using System;

namespace Utility.Logging;

public static class GeneralMessageCodes
{
	public static readonly MessageClass GenericLoadTime = new MessageClass("Generic_TaskLoadTime", MessageSeverity.Information, "{TaskName} loaded in {TimeSpan}.");

	public static readonly MessageClass ExceptionOccurred = new MessageClass("Generic_ExceptionOccurred", MessageSeverity.Error, "{TaskName} threw an unexpected error: {ExceptionMessage}");

	public static readonly MessageClass UserOperationCancelled = new MessageClass("Generic_TaskCancelled", MessageSeverity.Error, "{TaskName} was cancelled.");

	public static readonly MessageClass TaskStarted = new MessageClass("Generic_TaskStarted", MessageSeverity.Information, "{TaskName} started.");

	public static readonly MessageClass TaskCompleted = new MessageClass("Generic_TaskCompleted", MessageSeverity.Information, "{TaskName} completed.");

	public static readonly MessageClass PropertyValueError = new MessageClass("Generic_PropertyValueError", MessageSeverity.Error, "{ObjectName}.{PropertyName} does not match the expected value. Expected: {ExpectedValue}, Actual: {ActualValue}");

	public static readonly MessageClass TimeoutOccurred = new MessageClass("Generic_TimeoutOccurred", MessageSeverity.Error, "{TaskName} timed out.");

	public static Message GetGenericLoadTime(string task, TimeSpan ts)
	{
		return GenericLoadTime.Create(task, ts);
	}

	public static Message GetExceptionOccurred(string task, Exception ex)
	{
		return ExceptionOccurred.Create(task, ex.Message);
	}

	public static Message GetUserOperationCancelled(string task)
	{
		return UserOperationCancelled.Create(task);
	}

	public static Message GetTaskStarted(string task)
	{
		return TaskStarted.Create(task);
	}

	public static Message GetTaskCompleted(string task)
	{
		return TaskCompleted.Create(task);
	}

	public static Message GetPropertyValueMismatch(string objectName, string propertyName, object expectedValue, object actualValue)
	{
		return PropertyValueError.Create(objectName, propertyName, expectedValue, actualValue);
	}

	public static Message GetTimeoutOccurred(string task)
	{
		return TimeoutOccurred.Create(task);
	}
}
