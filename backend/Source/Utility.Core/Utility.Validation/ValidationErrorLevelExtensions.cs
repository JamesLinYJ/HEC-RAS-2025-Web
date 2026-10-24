using System;
using Utility.Logging;

namespace Utility.Validation;

public static class ValidationErrorLevelExtensions
{
	public static MessageSeverity AsSeverity(this ValidationErrorLevel level)
	{
		return level switch
		{
			ValidationErrorLevel.Success => MessageSeverity.Information, 
			ValidationErrorLevel.Warn => MessageSeverity.Warning, 
			ValidationErrorLevel.Error => MessageSeverity.Error, 
			ValidationErrorLevel.TypeError => MessageSeverity.Error, 
			_ => throw new NotImplementedException("We don't have a message severity for " + level), 
		};
	}
}
