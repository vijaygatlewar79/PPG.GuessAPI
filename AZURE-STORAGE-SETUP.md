# Azure Blob Storage setup

The API uses the private `files` container in the `ppgguessstorage` account in both
Development and Production. Excel workbooks must be uploaded at the container root
as `.xlsx` or `.xlsm` blobs. The API stores the mutable source catalog in
`chart-sources.json` and dated workbook backups below `backups/` in the same
container.

Upload the game workbooks before testing; when the container has no Excel blobs,
the API correctly returns an empty game list.

## Production (Azure App Service)

1. Enable the App Service's system-assigned managed identity.
2. On the `ppgguessstorage` storage account (or only its `files` container), add a
   role assignment for that identity with the **Storage Blob Data Contributor** role.
3. Restart the App Service after the role assignment has propagated.

No storage key is needed in Production when managed identity is configured.

## Development

Sign in to Visual Studio's Azure account or run `az login`. The signed-in identity
must also have the **Storage Blob Data Contributor** role on the storage account or
container. `DefaultAzureCredential` then uses that development identity.

For a local-only fallback, store a connection string with .NET user secrets. Never
put it in a tracked settings file:

```powershell
dotnet user-secrets set "AzureStorage:ConnectionString" "<storage-connection-string>"
```

The equivalent App Service setting name is
`AzureStorage__ConnectionString`, but managed identity is preferred.

## Non-secret configuration

The checked-in `appsettings.json` contains only these non-secret values:

```json
"AzureStorage": {
  "AccountName": "ppgguessstorage",
  "ContainerName": "files",
  "CatalogBlobName": "chart-sources.json",
  "BackupPrefix": "backups"
}
```

If a user-assigned identity is used instead, provide its client ID with the App
Service setting `AzureStorage__ManagedIdentityClientId`.
