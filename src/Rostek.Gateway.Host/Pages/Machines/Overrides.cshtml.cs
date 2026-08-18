using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.MachineTemplates;

namespace Rostek.Gateway.Host.Pages.Machines;

public sealed class OverridesModel(IMachineService machineService, IMachineTemplateService templateService, ISignalOverrideService overrideService) : PageModel
{
    public IReadOnlyList<TemplateSignalListItem> TemplateSignals { get; private set; } = [];
    public List<SelectListItem> SignalOptions { get; private set; } = [];
    public Guid TemplateId { get; private set; }

    [BindProperty]
    public SignalOverrideInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var machine = await machineService.GetInputAsync(id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        await LoadAsync(machine.TemplateId, id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var machine = await machineService.GetInputAsync(id, cancellationToken);
        if (machine is null)
        {
            return NotFound();
        }

        Input.MachineId = id;
        var result = await overrideService.SaveAsync(Input, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
            await LoadAsync(machine.TemplateId, id, cancellationToken);
            return Page();
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostResetAsync(Guid id, Guid templateSignalId, CancellationToken cancellationToken)
    {
        await overrideService.ResetAsync(id, templateSignalId, User.Identity?.Name, cancellationToken);
        return RedirectToPage(new { id });
    }

    private async Task LoadAsync(Guid templateId, Guid machineId, CancellationToken cancellationToken)
    {
        TemplateId = templateId;
        Input.MachineId = machineId;
        TemplateSignals = await templateService.ListSignalsAsync(templateId, cancellationToken);
        SignalOptions = TemplateSignals.Select(signal => new SelectListItem(signal.SignalCode, signal.Id.ToString())).ToList();
    }
}
