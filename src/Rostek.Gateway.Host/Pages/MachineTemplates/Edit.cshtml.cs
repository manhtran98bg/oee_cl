using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.MachineTemplates;
using Rostek.Gateway.Application.Ports;

namespace Rostek.Gateway.Host.Pages.MachineTemplates;

public sealed class EditModel(IMachineTemplateService service, IMachineService machineService) : PageModel
{
    [BindProperty]
    public MachineTemplateInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }
    public IReadOnlyList<MachineListItem> AssignedMachines { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is null)
        {
            return Page();
        }

        var input = await service.GetInputAsync(id.Value, cancellationToken);
        if (input is null)
        {
            return NotFound();
        }

        Input = input;
        await LoadAssignedMachinesAsync(id.Value, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await service.SaveAsync(Input, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
            if (Input.Id is Guid templateId)
            {
                await LoadAssignedMachinesAsync(templateId, cancellationToken);
            }
            return Page();
        }

        return RedirectToPage("Signals", new { id = result.Value });
    }

    private async Task LoadAssignedMachinesAsync(Guid templateId, CancellationToken cancellationToken)
    {
        var result = await machineService.ListAsync(
            new MachineQuery(null, null, templateId, null, null, 1, 200),
            cancellationToken);
        AssignedMachines = result.Items;
    }
}
