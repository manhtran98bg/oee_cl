using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Configurations;

namespace Rostek.Gateway.Host.Pages.Configuration;

public sealed class ExportModel(IConfigurationImportExportService importExport) : PageModel
{
    public void OnGet()
    {
    }

    public async Task<FileResult> OnGetActiveAsync(CancellationToken cancellationToken)
    {
        var json = await importExport.ExportActiveJsonAsync(cancellationToken);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "rostek-active-config.json");
    }

    public async Task<FileResult> OnGetDraftAsync(CancellationToken cancellationToken)
    {
        var json = await importExport.ExportDraftJsonAsync(cancellationToken);
        return File(Encoding.UTF8.GetBytes(json), "application/json", "rostek-draft-config.json");
    }
}
