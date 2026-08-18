using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Configurations;
using Rostek.Gateway.Contracts.Configuration;
using ConfigurationBuilderPort = Rostek.Gateway.Application.Configurations.IConfigurationBuilder;

namespace Rostek.Gateway.Host.Pages.Configuration;

public sealed class IndexModel(ConfigurationBuilderPort builder, IConfigurationValidator validator, IConfigurationApplyService applyService) : PageModel
{
    public ConfigurationValidationResult? Validation { get; private set; }
    public string? ApplyMessage { get; private set; }
    public bool ApplySucceeded { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostValidateAsync(CancellationToken cancellationToken)
    {
        Validation = await validator.ValidateAsync(await builder.BuildDraftAsync(cancellationToken), cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(string? description, CancellationToken cancellationToken)
    {
        var result = await applyService.ApplyDraftAsync(description, User.Identity?.Name, cancellationToken);
        Validation = result.Validation;
        ApplySucceeded = result.Succeeded;
        ApplyMessage = result.Succeeded ? $"Applied version {result.Version}." : result.ErrorMessage;
        return Page();
    }
}
