namespace Utility.Logging;

public class MessageClass
{
	public static readonly MessageClass NullTrace = new MessageClass("NULL_TRACE", MessageSeverity.Trace, string.Empty);

	public static readonly MessageClass NullDebug = new MessageClass("NULL_DEBUG", MessageSeverity.Debug, string.Empty);

	public static readonly MessageClass NullInformation = new MessageClass("NULL_INFORMATION", MessageSeverity.Information, string.Empty);

	public static readonly MessageClass NullWarning = new MessageClass("NULL_WARNING", MessageSeverity.Warning, string.Empty);

	public static readonly MessageClass NullError = new MessageClass("NULL_ERROR", MessageSeverity.Error, string.Empty);

	public string Code { get; }

	public MessageSeverity DefaultSeverity { get; }

	public string DefaultFormattedMessage { get; }

	public static bool IsNull(MessageClass mc)
	{
		if (mc != NullTrace && mc != NullDebug && mc != NullInformation && mc != NullWarning)
		{
			return mc == NullError;
		}
		return true;
	}

	public MessageClass(string code, MessageSeverity severity, string message)
	{
		Code = code;
		DefaultSeverity = severity;
		DefaultFormattedMessage = message;
	}

	public Message Create(params object[] data)
	{
		return new Message(this, data);
	}
}
