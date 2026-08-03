public static class SttDatasetValidation
{
    public static void Validate(SttDataset dataset)
    {
        if (string.IsNullOrWhiteSpace(dataset.Id)) throw new InvalidDataException("STT dataset id is required.");
        if (dataset.Gate is null) throw new InvalidDataException("STT dataset gate is required.");
        if (dataset.Cases is null) throw new InvalidDataException("STT dataset cases are required.");
        if (dataset.Gate.MinimumCases < 1) throw new InvalidDataException("STT minimumCases must be positive.");
        if (dataset.Cases.Count < dataset.Gate.MinimumCases)
            throw new InvalidDataException($"STT dataset contains {dataset.Cases.Count} cases; at least {dataset.Gate.MinimumCases} are required.");
        if (dataset.Gate.MaximumAverageWordErrorRate is < 0 or > 1)
            throw new InvalidDataException("STT maximumAverageWordErrorRate must be between 0 and 1.");
        if (dataset.Gate.MinimumTermRecall is < 0 or > 1)
            throw new InvalidDataException("STT minimumTermRecall must be between 0 and 1.");
        if (dataset.Gate.MaximumP95LatencyMs <= 0) throw new InvalidDataException("STT maximumP95LatencyMs must be positive.");
        if (dataset.Gate.MaximumMemoryBytes < 256L * 1024 * 1024)
            throw new InvalidDataException("STT maximumMemoryBytes must be at least 256 MiB.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in dataset.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id)) throw new InvalidDataException("Every STT case requires an id.");
            if (!ids.Add(item.Id)) throw new InvalidDataException($"Duplicate STT case id: {item.Id}");
            if (string.IsNullOrWhiteSpace(item.AudioFile)) throw new InvalidDataException($"STT case '{item.Id}' requires audioFile.");
            if (string.IsNullOrWhiteSpace(item.Reference)) throw new InvalidDataException($"STT case '{item.Id}' requires reference text.");
            if (item.RequiredTerms is null) throw new InvalidDataException($"STT case '{item.Id}' requires requiredTerms (an empty array is allowed).");
            if (item.RequiredTerms.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException($"STT case '{item.Id}' contains an empty required term.");
        }
    }
}
