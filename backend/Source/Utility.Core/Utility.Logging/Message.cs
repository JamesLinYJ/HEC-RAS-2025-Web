using System.Collections.Generic;
using System.Diagnostics;

namespace Utility.Logging;

[DebuggerDisplay("{ToDebuggerDisplay()},nq")]
public struct Message
{
	private string _evaluatedMessage;

	public MessageClass MessageType { get; }

	public string EvaluatedMessage
	{
		get
		{
			if (string.IsNullOrEmpty(_evaluatedMessage))
			{
				_evaluatedMessage = LogData.ToString();
			}
			return _evaluatedMessage;
		}
	}

	public FormattedLogValues LogData { get; private set; }

	public Message(MessageClass type, params object[] data)
	{
		_evaluatedMessage = null;
		MessageType = type;
		LogData = new FormattedLogValues(type.DefaultFormattedMessage, data);
	}

	public Message(MessageClass type, string altMessage, params object[] data)
	{
		_evaluatedMessage = null;
		MessageType = type;
		LogData = new FormattedLogValues(altMessage, data);
	}

	public Message(string formattedMessage, params object[] data)
	{
		_evaluatedMessage = null;
		MessageType = MessageClass.NullInformation;
		LogData = new FormattedLogValues(formattedMessage, data);
	}

	public static Message GenericError(string formattedMessage, params object[] data)
	{
		return new Message(MessageClass.NullError, formattedMessage, data);
	}

	public object GetArgumentByName(string stringValue)
	{
		foreach (KeyValuePair<string, object> logDatum in LogData)
		{
			if (logDatum.Key == stringValue)
			{
				return logDatum.Value;
			}
		}
		return null;
	}

	public override int GetHashCode()
	{
		return EvaluatedMessage.GetHashCode();
	}

	public override bool Equals(object obj)
	{
		if (!(obj is Message message))
		{
			return false;
		}
		return string.Equals(message.EvaluatedMessage, EvaluatedMessage);
	}

	public string ToDebuggerDisplay()
	{
		return MessageType.Code + " (" + SeverityToShortString(MessageType.DefaultSeverity) + "): " + EvaluatedMessage;
	}

	private static string SeverityToShortString(MessageSeverity sev)
	{
		return sev switch
		{
			MessageSeverity.Error => "ERR", 
			MessageSeverity.Warning => "WRN", 
			MessageSeverity.Information => "INF", 
			MessageSeverity.Debug => "DBG", 
			MessageSeverity.Trace => "TRC", 
			_ => "N/A", 
		};
	}
}
