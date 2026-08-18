using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Dashboard;

namespace Rostek.Gateway.Host.Pages;

public sealed class IndexModel(IDashboardService dashboardService) : PageModel
{
    public DashboardDto Dashboard { get; private set; } =
        new(new DashboardSummaryDto(0, 0, 0, 0, 0, 0), []);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Dashboard = await dashboardService.GetAsync(cancellationToken);
    }
}
