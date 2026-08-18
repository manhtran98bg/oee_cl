using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.Ports;

namespace Rostek.Gateway.Host.Pages.Machines;

public sealed class IndexModel(IMachineService service) : PageModel
{
    public MachineListResult Machines { get; private set; } = new([], 1, 50);

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Machines = await service.ListAsync(new MachineQuery(Search, null, null, null, null, 1, 50), cancellationToken);
    }
}
