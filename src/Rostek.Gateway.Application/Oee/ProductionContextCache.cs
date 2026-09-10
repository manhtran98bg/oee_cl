using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Application.Oee;

public interface IProductionContextCache
{
    IReadOnlyList<ProductionContext> Current { get; }
    ProductionContext? Get(string sessionId);
    ProductionContext? GetActive(string machine, string orderId);
    IReadOnlyList<ProductionContext> GetCapturableByMachine(string machine);
    void Replace(IReadOnlyCollection<ProductionContext> contexts);
    void Upsert(ProductionContext context);
    void Remove(string sessionId);
}

public sealed class ProductionContextCache : IProductionContextCache
{
    private readonly object _lock = new();
    private Dictionary<string, ProductionContext> _contextsBySession = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ProductionContext> Current
    {
        get
        {
            lock (_lock)
            {
                return _contextsBySession.Values.Select(Clone).ToList();
            }
        }
    }

    public ProductionContext? Get(string sessionId)
    {
        lock (_lock)
        {
            return _contextsBySession.TryGetValue(sessionId, out var context) ? Clone(context) : null;
        }
    }

    public ProductionContext? GetActive(string machine, string orderId)
    {
        lock (_lock)
        {
            return _contextsBySession.Values
                .Where(context =>
                    context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) &&
                    context.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase) &&
                    IsCapturable(context.Status))
                .OrderByDescending(context => context.ActivePeriodStartAt)
                .Select(Clone)
                .FirstOrDefault();
        }
    }

    public IReadOnlyList<ProductionContext> GetCapturableByMachine(string machine)
    {
        lock (_lock)
        {
            return _contextsBySession.Values
                .Where(context => context.Machine.Equals(machine, StringComparison.OrdinalIgnoreCase) && IsCapturable(context.Status))
                .OrderBy(context => context.OrderId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(context => context.SessionId, StringComparer.OrdinalIgnoreCase)
                .Select(Clone)
                .ToList();
        }
    }

    public void Replace(IReadOnlyCollection<ProductionContext> contexts)
    {
        lock (_lock)
        {
            _contextsBySession = contexts
                .Where(context => !string.IsNullOrWhiteSpace(context.SessionId))
                .ToDictionary(context => context.SessionId, Clone, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Upsert(ProductionContext context)
    {
        lock (_lock)
        {
            _contextsBySession[context.SessionId] = Clone(context);
        }
    }

    public void Remove(string sessionId)
    {
        lock (_lock)
        {
            _contextsBySession.Remove(sessionId);
        }
    }

    private static bool IsCapturable(string status) =>
        status.Equals("active", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("pause", StringComparison.OrdinalIgnoreCase);

    private static ProductionContext Clone(ProductionContext context) =>
        new()
        {
            SessionId = context.SessionId,
            Machine = context.Machine,
            Status = context.Status,
            OrderId = context.OrderId,
            ServerOrderId = context.ServerOrderId,
            ActivePeriodStartAt = context.ActivePeriodStartAt,
            CurrentPlcPeriodIndex = context.CurrentPlcPeriodIndex,
            ProductsJson = context.ProductsJson,
            ExtraJson = context.ExtraJson,
            BaselineRawId = context.BaselineRawId,
            BaselineCapturedAt = context.BaselineCapturedAt,
            BaselineShotOkTotal = context.BaselineShotOkTotal,
            BaselineShotNgTotal = context.BaselineShotNgTotal,
            BaselineRunTimeTotalSec = context.BaselineRunTimeTotalSec,
            BaselineStopTimeTotalSec = context.BaselineStopTimeTotalSec,
            BaselineErrorTimeTotalSec = context.BaselineErrorTimeTotalSec,
            BaselineCycleTimeMs = context.BaselineCycleTimeMs,
            UpdatedAt = context.UpdatedAt
        };
}
