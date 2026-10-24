using Utility.Logging;

namespace Utility.Math.x86;

public class MatrixMessageDefinition
{
	public MessageClass MessageClass { get; }

	public MatrixMessageDefinition(string code, MessageSeverity severity, string format)
	{
		MessageClass = new MessageClass(code, severity, format);
	}

	public Message Create()
	{
		return MessageClass.Create();
	}

	public Message Create(object localValue)
	{
		return MessageClass.Create(localValue);
	}

	public Message Create(object localValue, object localValue2)
	{
		return MessageClass.Create(localValue, localValue2);
	}

	public Message Create(object localValue, object localValue2, object localValue3, object localValue4)
	{
		return MessageClass.Create(localValue, localValue2, localValue3, localValue4);
	}
}
