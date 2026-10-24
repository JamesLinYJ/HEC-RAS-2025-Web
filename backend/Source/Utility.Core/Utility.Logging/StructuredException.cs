using System;
using System.Collections.Generic;
using System.Linq;

namespace Utility.Logging;

public class StructuredException : Exception
{
	public List<Message> Messages { get; }

	public StructuredException(Message message)
		: base(message.EvaluatedMessage)
	{
		Messages = new List<Message>();
		Messages.Add(message);
	}

	public StructuredException(OperationResult or)
		: base(or.GetConcatenatedMessages())
	{
		Messages = or.Messages.ToList();
	}

	public StructuredException(IList<Message> messages)
		: base(string.Join(Environment.NewLine, messages.Select((Message msg) => msg.EvaluatedMessage)))
	{
		Messages = messages.ToList();
	}
}
