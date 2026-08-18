using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.MachineGroups;

namespace Rostek.Gateway.Host.Pages.MachineGroups;

public sealed class IndexModel(IMachineGroupService service) : PageModel
{
    public IReadOnlyList<MachineGroupListItem> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Groups = await service.ListAsync(cancellationToken);
    }
}
