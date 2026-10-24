namespace Ras.Process.RasterCommands.Explorer;

public record TiffTagEntry(string Name, string Summary, string FullValue, string Description, bool IsEditable, int TagId, string EditHint);
