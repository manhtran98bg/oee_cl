using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.MachineGroups;

namespace Rostek.Gateway.Host.Pages.MachineGroups;

public sealed class EditModel(IMachineGroupService service) : PageModel
{
    [BindProperty]
    public MachineGroupInput Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

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
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await service.SaveAsync(Input, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        return RedirectToPage("Index");
    }
}
