using System.Globalization;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using PPG.GuessData;

namespace PPG.GuessAPI;

public sealed class AzureBlobPanelFileStorage : IPanelFileStorage
{
    private static readonly TimeSpan IndiaStandardTimeOffset = TimeSpan.FromMinutes(330);

    private readonly BlobContainerClient _containerClient;
    private readonly string _catalogBlobName;
    private readonly string _backupPrefix;
    private ETag? _catalogETag;

    public AzureBlobPanelFileStorage(IOptions<AzureBlobStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.ContainerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.CatalogBlobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.BackupPrefix);

        var serviceClient = CreateServiceClient(configuration);
        _containerClient = serviceClient.GetBlobContainerClient(configuration.ContainerName.Trim());
        _catalogBlobName = ValidateBlobName(
            configuration.CatalogBlobName,
            nameof(configuration.CatalogBlobName));
        _backupPrefix = ValidateBlobPrefix(
            configuration.BackupPrefix,
            nameof(configuration.BackupPrefix));
    }

    public async Task<IReadOnlyList<string>> ListExcelFileNamesAsync(
        CancellationToken cancellationToken = default)
    {
        var fileNames = new List<string>();

        await foreach (var item in _containerClient.GetBlobsByHierarchyAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           delimiter: "/",
                           prefix: null,
                           cancellationToken: cancellationToken))
        {
            if (item.IsBlob && IsRootExcelFileName(item.Blob.Name))
            {
                fileNames.Add(item.Blob.Name);
            }
        }

        fileNames.Sort(StringComparer.OrdinalIgnoreCase);
        return fileNames;
    }

    public async Task<Stream> OpenExcelFileAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var validatedFileName = ValidateRootExcelFileName(fileName);

        try
        {
            var download = await _containerClient
                .GetBlobClient(validatedFileName)
                .DownloadContentAsync(cancellationToken);
            return new MemoryStream(download.Value.Content.ToArray(), writable: false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new FileNotFoundException(
                $"The Excel file '{validatedFileName}' was not found in Azure Blob Storage.",
                validatedFileName,
                exception);
        }
    }

    public async Task<bool> ExcelFileExistsAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var validatedFileName = ValidateRootExcelFileName(fileName);
        var response = await _containerClient
            .GetBlobClient(validatedFileName)
            .ExistsAsync(cancellationToken);
        return response.Value;
    }

    public async Task SaveExcelFileAsync(
        string fileName,
        ReadOnlyMemory<byte> content,
        ExcelFileBackupAction backupAction,
        CancellationToken cancellationToken = default)
    {
        var validatedFileName = ValidateRootExcelFileName(fileName);
        var blobClient = _containerClient.GetBlobClient(validatedFileName);
        var existingContent = await DownloadIfExistsAsync(blobClient, cancellationToken);

        if (existingContent is not null)
        {
            await BackupAsync(
                validatedFileName,
                existingContent.Content,
                backupAction,
                cancellationToken);
        }

        var conditions = existingContent is null
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = existingContent.ETag };

        await blobClient.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = GetExcelContentType(validatedFileName)
                },
                Conditions = conditions
            },
            cancellationToken);
    }

    public async Task DeleteExcelFileAsync(
        string fileName,
        ExcelFileBackupAction backupAction,
        CancellationToken cancellationToken = default)
    {
        var validatedFileName = ValidateRootExcelFileName(fileName);
        var blobClient = _containerClient.GetBlobClient(validatedFileName);
        var existingContent = await DownloadIfExistsAsync(blobClient, cancellationToken);

        if (existingContent is null)
        {
            return;
        }

        await BackupAsync(
            validatedFileName,
            existingContent.Content,
            backupAction,
            cancellationToken);
        await blobClient.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            conditions: new BlobRequestConditions { IfMatch = existingContent.ETag },
            cancellationToken: cancellationToken);
    }

    public async Task<string?> ReadCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var download = await _containerClient
                .GetBlobClient(_catalogBlobName)
                .DownloadContentAsync(cancellationToken);
            _catalogETag = download.Value.Details.ETag;
            return download.Value.Content.ToString();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            _catalogETag = null;
            return null;
        }
    }

    public async Task WriteCatalogAsync(
        string catalogJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogJson);

        var conditions = _catalogETag.HasValue
            ? new BlobRequestConditions { IfMatch = _catalogETag.Value }
            : new BlobRequestConditions { IfNoneMatch = ETag.All };
        var response = await _containerClient
            .GetBlobClient(_catalogBlobName)
            .UploadAsync(
                BinaryData.FromString(catalogJson),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = "application/json; charset=utf-8"
                    },
                    Conditions = conditions
                },
                cancellationToken);
        _catalogETag = response.Value.ETag;
    }

    private async Task BackupAsync(
        string fileName,
        BinaryData content,
        ExcelFileBackupAction action,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToOffset(IndiaStandardTimeOffset);
        var backupFileName = string.Concat(
            Path.GetFileNameWithoutExtension(fileName),
            "_",
            timestamp.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture),
            "_",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            "_IST",
            Path.GetExtension(fileName));
        var backupBlobName = string.Join(
            '/',
            _backupPrefix,
            timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            action.ToString(),
            backupFileName);

        await _containerClient
            .GetBlobClient(backupBlobName)
            .UploadAsync(
                content,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = GetExcelContentType(fileName)
                    },
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                },
                cancellationToken);
    }

    private static async Task<StoredBlobContent?> DownloadIfExistsAsync(
        BlobClient blobClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var download = await blobClient.DownloadContentAsync(cancellationToken);
            return new StoredBlobContent(
                download.Value.Content,
                download.Value.Details.ETag);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private static BlobServiceClient CreateServiceClient(AzureBlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString.Trim());
        }

        var serviceUri = GetServiceUri(options);
        var credentialOptions = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            credentialOptions.ManagedIdentityClientId = options.ManagedIdentityClientId.Trim();
        }

        return new BlobServiceClient(
            serviceUri,
            new DefaultAzureCredential(credentialOptions));
    }

    private static Uri GetServiceUri(AzureBlobStorageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceUri))
        {
            if (Uri.TryCreate(options.ServiceUri.Trim(), UriKind.Absolute, out var configuredUri))
            {
                return configuredUri;
            }

            throw new ArgumentException(
                "AzureStorage:ServiceUri must be an absolute URI.",
                nameof(options.ServiceUri));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(options.AccountName);
        return new Uri(
            $"https://{options.AccountName.Trim()}.blob.core.windows.net",
            UriKind.Absolute);
    }

    private static string ValidateRootExcelFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!string.Equals(fileName, fileName.Trim(), StringComparison.Ordinal)
            || !IsRootExcelFileName(fileName))
        {
            throw new ArgumentException(
                "The file name must be a non-temporary root .xlsx or .xlsm file name.",
                nameof(fileName));
        }

        return fileName;
    }

    private static bool IsRootExcelFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny(['/', '\\']) >= 0
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.StartsWith(".", StringComparison.Ordinal)
            || fileName.StartsWith("~$", StringComparison.Ordinal))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExcelContentType(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".xlsm", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.ms-excel.sheet.macroEnabled.12"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static string ValidateBlobName(string blobName, string parameterName)
    {
        var value = blobName.Trim().Trim('/');
        if (value.Length == 0
            || value.Contains('\\', StringComparison.Ordinal)
            || HasRelativePathSegment(value))
        {
            throw new ArgumentException("The configured blob name is invalid.", parameterName);
        }

        return value;
    }

    private static string ValidateBlobPrefix(string prefix, string parameterName)
    {
        var value = prefix.Trim().Trim('/');
        if (value.Length == 0
            || value.Contains('\\', StringComparison.Ordinal)
            || HasRelativePathSegment(value))
        {
            throw new ArgumentException("The configured blob prefix is invalid.", parameterName);
        }

        return value;
    }

    private static bool HasRelativePathSegment(string value) =>
        value.Split('/', StringSplitOptions.None)
            .Any(segment => segment.Length == 0 || segment is "." or "..");

    private sealed record StoredBlobContent(BinaryData Content, ETag ETag);
}
