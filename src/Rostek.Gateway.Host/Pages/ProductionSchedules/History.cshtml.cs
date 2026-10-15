using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Scheduling;

namespace Rostek.Gateway.Host.Pages.ProductionSchedules;

public sealed class HistoryModel(IProductionScheduleService service) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid? ScheduleId { get; set; }
    public IReadOnlyList<ProductionScheduleHistory> History { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken cancellationToken) => History = await service.HistoryAsync(ScheduleId, cancellationToken);
}
