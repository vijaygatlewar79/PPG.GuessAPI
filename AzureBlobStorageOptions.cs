namespace PPG.GuessAPI;

public sealed class AzureBlobStorageOptions
{
    public const string SectionName = "AzureStorage";

    public string AccountName { get; init; } = "ppgguessstorage";

    public string ContainerName { get; init; } = "files";

    public string CatalogBlobName { get; init; } = "chart-sources.json";

    public string BackupPrefix { get; init; } = "backups";

    public string? ConnectionString { get; init; }

    public string? ServiceUri { get; init; }

    public string? ManagedIdentityClientId { get; init; }
}
