namespace Ras.Backend.Server;

internal static class SourceUploadNames
{
    internal static void ValidatePortableSegment(string name,
        string invalidNameMessage = "Source files must have portable file names without directories.",
        string reservedNameMessage = "A selected file name is reserved by supported desktop platforms.",
        int maximumLength = 240)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > maximumLength ||
            name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)) ||
            name.EndsWith('.') || name.EndsWith(' '))
            throw new InvalidDataException(invalidNameMessage);

        string deviceName = name.Split('.')[0].ToUpperInvariant();
        if (deviceName is "CON" or "PRN" or "AUX" or "NUL" ||
            deviceName.Length == 4 &&
            (deviceName.StartsWith("COM", StringComparison.Ordinal) ||
             deviceName.StartsWith("LPT", StringComparison.Ordinal)) &&
            deviceName[3] is >= '1' and <= '9')
            throw new InvalidDataException(reservedNameMessage);
    }
}
