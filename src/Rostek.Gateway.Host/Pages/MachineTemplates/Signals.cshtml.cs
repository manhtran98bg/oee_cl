using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.MachineTemplates;

namespace Rostek.Gateway.Host.Pages.MachineTemplates;

public sealed class SignalsModel(IMachineTemplateService service) : PageModel
{
    public IReadOnlyList<TemplateSignalListItem> Signals { get; private set; } = [];

    [BindProperty]
    public TemplateSignalInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public bool IsEditing => Input.Id.HasValue;

    public string FormTitle => IsEditing ? $"Edit Signal: {Input.SignalCode}" : "Add Signal";

    public async Task OnGetAsync(Guid id, Guid? signalId, CancellationToken cancellationToken)
    {
        Input.TemplateId = id;
        Signals = await service.ListSignalsAsync(id, cancellationToken);
        if (signalId is not Guid editSignalId)
        {
            return;
        }

        var signalInput = await service.GetSignalInputAsync(id, editSignalId, cancellationToken);
        if (signalInput is null)
        {
            ErrorMessage = "Template signal not found.";
            return;
        }

        Input = signalInput;
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        Input.TemplateId = id;
        var result = await service.SaveSignalAsync(Input, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
            Signals = await service.ListSignalsAsync(id, cancellationToken);
            return Page();
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, Guid signalId, CancellationToken cancellationToken)
    {
        await service.DeleteSignalAsync(signalId, User.Identity?.Name, cancellationToken);
        return RedirectToPage(new { id });
    }
}
