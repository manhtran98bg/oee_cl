using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Rostek.Gateway.Application.Scheduling;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Host.Pages.ProductionSchedules;

public sealed class EditModel(IProductionScheduleService service) : PageModel
{
    [BindProperty] public ProductionScheduleInput Input { get; set; } = new();
    public IReadOnlyList<Machine> Machines { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is Guid scheduleId)
        {
            var input = await service.GetInputAsync(scheduleId, cancellationToken);
            if (input is null) return NotFound();
            Input = input;
        }
        else Input.Weekdays = [0, 1, 2, 3, 4, 5, 6];
        Machines = await service.ListMachinesAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await service.SaveAsync(Input, cancellationToken);
            if (result.Succeeded) return RedirectToPage("Index");
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không thể lưu lịch.");
        }
        Machines = await service.ListMachinesAsync(cancellationToken);
        return Page();
    }
}
