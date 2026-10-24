namespace H5Assist.Differences;

public readonly struct ExtractedDataset(string path, string hash, long onDiskBytes)
{
	public string Path { get; } = path;

	public string Hash { get; } = hash;

	public long OnDiskBytes { get; } = onDiskBytes;
}
