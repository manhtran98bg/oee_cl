using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IProductionContextCache
{
    IReadOnlyDictionary<string, ProductionContext> Current { get; }
    ProductionContext? Get(string machine);
    void Replace(IReadOnlyDictionary<string, ProductionContext> contexts);
    void Upsert(ProductionContext context);
}

public sealed class ProductionContextCache : IProductionContextCache
{
    private readonly object _lock = new();
    private Dictionary<string, ProductionContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, ProductionContext> Current
    {
        get
        {
            lock (_lock)
            {
                return _contexts.ToDictionary(item => item.Key, item => Clone(item.Value), StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public ProductionContext? Get(string machine)
    {
        lock (_lock)
        {
            return _contexts.TryGetValue(machine, out var context) ? Clone(context) : null;
        }
    }

    public void Replace(IReadOnlyDictionary<string, ProductionContext> contexts)
    {
        lock (_lock)
        {
            _contexts = contexts.ToDictionary(item => item.Key, item => Clone(item.Value), StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Upsert(ProductionContext context)
    {
        lock (_lock)
        {
            _contexts[context.Machine] = Clone(context);
        }
    }

    private static ProductionContext Clone(ProductionContext context) =>
        new()
        {
            Machine = context.Machine,
            Mode = context.Mode,
            OrderId = context.OrderId,
            ServerOrderId = context.ServerOrderId,
            ActivePeriodId = context.ActivePeriodId,
            CurrentPlcPeriodIndex = context.CurrentPlcPeriodIndex,
            ProductsJson = context.ProductsJson,
            TagsJson = context.TagsJson,
            ExtraJson = context.ExtraJson,
            Status = context.Status,
            UpdatedAt = context.UpdatedAt
        };
}
