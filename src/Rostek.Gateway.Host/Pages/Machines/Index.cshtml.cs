using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Host.Options;
using Microsoft.Extensions.Options;

namespace Rostek.Gateway.Host.Pages.Machines;

public sealed class IndexModel(
    IMachineService service,
    IMesProductionOrderService productionOrderService,
    IOptions<GatewayOptions> gatewayOptions) : PageModel
{
    public MachineListResult Machines { get; private set; } = new([], 1, 200);
    public MesProductionOrderSnapshot ProductionOrders { get; private set; } = MesProductionOrderSnapshot.Empty;

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Machines = await service.ListAsync(new MachineQuery(Search, null, null, null, null, 1, 200), cancellationToken);
        ProductionOrders = productionOrderService.GetSnapshot();
    }

    public IReadOnlyList<MesProductionOrderListItem> GetOrders(string machineCode) =>
        ProductionOrders.Items
            .Where(order => order.MachineCode.Equals(machineCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public async Task<IActionResult> OnPostFetchOrdersAsync(CancellationToken cancellationToken)
    {
        var result = await productionOrderService.RefreshAsync(cancellationToken);
        if (result.Succeeded)
        {
            SuccessMessage = $"Fetched {result.Value?.Items.Count ?? 0} producing orders from MES.";
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }

        return RedirectToPage(new { search = Search });
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
            ErrorMessage = result.ErrorMessage;
        }
        else
        {
            var itemMessages = string.Join(" ", result.Value.Items.Select(item =>
                $"{item.OrderId}: {(item.Accepted ? item.Message ?? "Accepted" : item.Message ?? "Rejected")}"));
            SuccessMessage = $"Sessions: {result.Value.AcceptedCount} accepted, {result.Value.RejectedCount} rejected. {itemMessages}";
        }

        return RedirectToPage(new { search = Search });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, User.Identity?.Name, cancellationToken);
        return RedirectToPage(new { search = Search });
    }

    public async Task<IActionResult> OnPostToggleEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var result = await service.SetEnabledAsync(id, enabled, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
        }

        return RedirectToPage(new { search = Search });
    }

    public async Task<IActionResult> OnPostSetAllEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var result = await service.SetAllEnabledAsync(enabled, User.Identity?.Name, cancellationToken);
        if (!result.Succeeded)
        {
            ErrorMessage = result.ErrorMessage;
        }

        return RedirectToPage(new { search = Search });
    }
}
