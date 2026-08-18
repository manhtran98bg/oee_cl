using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Rostek.Gateway.Application.MachineGroups;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.MachineTemplates;

namespace Rostek.Gateway.Host.Pages.Machines;

public sealed class EditModel(IMachineService machineService, IMachineGroupService groupService, IMachineTemplateService templateService) : PageModel
{
    [BindProperty]
    public MachineEditInput Input { get; set; } = new();

    public List<SelectListItem> GroupOptions { get; private set; } = [];
    public List<SelectListItem> TemplateOptions { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? EffectiveJson { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        await LoadOptionsAsync(cancellationToken);
        if (id is null)
        {
            return Page();
        }

        var input = await machineService.GetInputAsync(id.Value, cancellationToken);
        if (input is null)
        {
            return NotFound();
        }

        Input = input;
        var preview = await machineService.PreviewEffectiveAsync(id.Value, cancellationToken);
        EffectiveJson = preview is null ? null : JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadOptionsAsync(cancellationToken);
        var result = await machineService.SaveAsync(Input, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        return RedirectToPage(new { id = result.Value });
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        GroupOptions = (await groupService.ListAsync(cancellationToken))
            .Select(group => new SelectListItem(group.Code, group.Id.ToString()))
            .ToList();
        TemplateOptions = (await templateService.ListAsync(cancellationToken))
            .Select(template => new SelectListItem($"{template.Code} ({template.Protocol})", template.Id.ToString()))
            .ToList();
    }
}
