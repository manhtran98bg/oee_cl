namespace Rostek.Gateway.Contracts.ImportExport;

public sealed record ImportPreview(
    bool IsValid,
    IReadOnlyList<ImportPreviewItem> Items,
    IReadOnlyList<string> Errors,
    string SourceKind);

public sealed record ImportPreviewItem(
    string Action,
    string EntityType,
    string Code,
    string Message);
