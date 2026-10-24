namespace Utility.Logging;

public interface ILogTextFormatter
{
	void Format(in LogRecord record, ref ValueStringBuilder builder);
}
