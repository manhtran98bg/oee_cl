using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Configurations;

namespace Rostek.Gateway.Host.Pages.Configuration;

public sealed class VersionModel(IConfigurationVersionService versionService, IConfigurationApplyService applyService) : PageModel
{
    public long Version { get; private set; }
    public string? Json { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync(long version, CancellationToken cancellationToken)
    {
        Version = version;
        var snapshot = await versionService.GetVersionAsync(version, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        Json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long version, CancellationToken cancellationToken)
    {
        var result = await applyService.RollbackAsync(version, $"Rollback from version {version}", User.Identity?.Name, cancellationToken);
        Message = result.Succeeded ? $"Rollback applied as version {result.Version}." : result.ErrorMessage;
        return await OnGetAsync(version, cancellationToken);
    }
}
