namespace Utility.Logging;

public readonly record struct AggregatedMessage(Message Message, int ExtraCount)
{
	public string FormattedMessage
	{
		get
		{
			if (ExtraCount != 0)
			{
				return $"{Message.EvaluatedMessage} (and {ExtraCount} more)";
			}
			return Message.EvaluatedMessage;
		}
	}
}
