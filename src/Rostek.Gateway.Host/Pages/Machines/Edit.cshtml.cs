using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Rostek.Gateway.Application.MachineGroups;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.MachineTemplates;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Host.Options;
using Microsoft.Extensions.Options;

namespace Rostek.Gateway.Host.Pages.Machines;

public sealed class EditModel(
    IMachineService machineService,
    IMachineGroupService groupService,
    IMachineTemplateService templateService,
    IMesProductionOrderService productionOrderService,
    IOptions<GatewayOptions> gatewayOptions) : PageModel
{
    [BindProperty]
    public MachineEditInput Input { get; set; } = new();

    [BindProperty]
    public bool SelectedFromMes { get; set; }

    public List<SelectListItem> GroupOptions { get; private set; } = [];
    public List<SelectListItem> TemplateOptions { get; private set; } = [];
    public string TemplateProtocolsJson { get; private set; } = "{}";
    public string? ErrorMessage { get; private set; }
    public string? EffectiveJson { get; private set; }
    public IReadOnlyList<MesProductionOrderListItem> OfferedOrders { get; private set; } = [];

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, bool offerOrders, CancellationToken cancellationToken)
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
        if (offerOrders)
        {
            OfferedOrders = productionOrderService.GetByMachine(Input.Code);
        }
        var preview = await machineService.PreviewEffectiveAsync(id.Value, cancellationToken);
        EffectiveJson = preview is null ? null : JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        await LoadOptionsAsync(cancellationToken);
        var isNewMachine = Input.Id is null;
        var result = await machineService.SaveAsync(Input, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
            return Page();
        }

        return RedirectToPage(new { id = result.Value, offerOrders = isNewMachine && SelectedFromMes });
    }

    public async Task<IActionResult> OnPostCreateSessionsAsync(
        string machineCode,
        List<string> orderIds,
        CancellationToken cancellationToken)
    {
        var result = await productionOrderService.CreateSessionsAsync(
            gatewayOptions.Value.GatewayId,
            machineCode,
            orderIds,
            cancellationToken);

        if (!result.Succeeded || result.Value is null)
        {
            TempData["ErrorMessage"] = result.ErrorMessage;
        }
        else
        {
            SuccessMessage = $"Sessions: {result.Value.AcceptedCount} accepted, {result.Value.RejectedCount} rejected.";
        }

        return RedirectToPage("Index");
    }

    private async Task LoadOptionsAsync(CancellationToken cancellationToken)
    {
        GroupOptions = (await groupService.ListAsync(cancellationToken))
            .Select(group => new SelectListItem(group.Code, group.Id.ToString()))
            .ToList();
        var templates = await templateService.ListAsync(cancellationToken);
        TemplateOptions = templates
            .Select(template => new SelectListItem($"{template.Code} ({template.Protocol})", template.Id.ToString()))
            .ToList();
        TemplateProtocolsJson = JsonSerializer.Serialize(
            templates.ToDictionary(template => template.Id.ToString(), template => template.Protocol.ToString()));
    }
}
