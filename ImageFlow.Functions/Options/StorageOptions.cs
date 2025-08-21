namespace ImageFlow.Functions.Options;

public sealed record StorageOptions(
    string StorageAccountUrl,
    string InputContainer,
    string ThumbnailContainer,
    string AnalysisContainer);
