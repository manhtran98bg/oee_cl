using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Configurations;
using Rostek.Gateway.Contracts.ImportExport;

namespace Rostek.Gateway.Host.Pages.Configuration;

public sealed class ImportModel(IConfigurationImportExportService importExport) : PageModel
{
    [BindProperty]
    public string Csv { get; set; } = "machineCode,name,groupCode,templateCode,host,port,unitId,endpointUrl,enabled";

    [BindProperty]
    public bool CreateMissingGroups { get; set; } = true;

    [BindProperty]
    public bool UpdateExisting { get; set; } = true;

    public ImportPreview? Preview { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        Preview = await importExport.PreviewMachinesCsvAsync(Csv, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCommitAsync(CancellationToken cancellationToken)
    {
        Preview = await importExport.CommitMachinesCsvAsync(Csv, CreateMissingGroups, UpdateExisting, User.Identity?.Name, cancellationToken);
        return Page();
    }
}
