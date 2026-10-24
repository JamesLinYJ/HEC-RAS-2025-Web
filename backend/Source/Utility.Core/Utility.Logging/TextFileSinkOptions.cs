namespace Utility.Logging;

public sealed class TextFileSinkOptions : RollingFileSinkOptions
{
	public ILogTextFormatter Formatter { get; set; } = new TextLogFormatter();
}
