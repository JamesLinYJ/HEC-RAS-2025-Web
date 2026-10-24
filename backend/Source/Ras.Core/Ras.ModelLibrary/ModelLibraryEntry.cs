using System;

namespace Ras.ModelLibrary;

public readonly record struct ModelLibraryEntry(string Id, long SizeBytes, DateTime LastModified, string Owner);
