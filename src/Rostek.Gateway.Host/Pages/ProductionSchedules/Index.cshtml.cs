using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Scheduling;

namespace Rostek.Gateway.Host.Pages.ProductionSchedules;

public sealed class IndexModel(IProductionScheduleService service) : PageModel
{
    public IReadOnlyList<ProductionScheduleListItem> Schedules { get; private set; } = [];
    [TempData] public string? ErrorMessage { get; set; }
    public async Task OnGetAsync(CancellationToken cancellationToken) => Schedules = await service.ListAsync(cancellationToken);

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool enabled, CancellationToken cancellationToken)
    {
        var result = await service.SetEnabledAsync(id, enabled, cancellationToken);
        if (!result.Succeeded) ErrorMessage = result.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        if (!result.Succeeded) ErrorMessage = result.ErrorMessage;
        return RedirectToPage();
    }
}
