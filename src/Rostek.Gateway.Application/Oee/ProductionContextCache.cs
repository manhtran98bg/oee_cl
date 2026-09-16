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

public interface IOrderQuantityCache
{
    long GetCompletedQty(string machine, string orderId);
    void ReplaceCompletedSessions(IReadOnlyCollection<ProductionMetric> finalSessionMetrics);
    bool AddCompletedSession(string machine, string orderId, string sessionId, long actualQty);
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

public sealed class OrderQuantityCache : IOrderQuantityCache
{
    private readonly object _lock = new();
    private Dictionary<string, long> _completedQtyByOrder = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _appliedSessionKeys = new(StringComparer.OrdinalIgnoreCase);

    public long GetCompletedQty(string machine, string orderId)
    {
        lock (_lock)
        {
            return _completedQtyByOrder.TryGetValue(OrderKey(machine, orderId), out var qty) ? qty : 0;
        }
    }

    public void ReplaceCompletedSessions(IReadOnlyCollection<ProductionMetric> finalSessionMetrics)
    {
        lock (_lock)
        {
            _completedQtyByOrder = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            _appliedSessionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var metric in finalSessionMetrics.Where(IsFinalSessionMetric))
            {
                var sessionKey = SessionKey(metric.Machine, metric.OrderId, metric.SessionId!);
                if (!_appliedSessionKeys.Add(sessionKey))
                {
                    continue;
                }

                var orderKey = OrderKey(metric.Machine, metric.OrderId);
                _completedQtyByOrder[orderKey] = _completedQtyByOrder.GetValueOrDefault(orderKey) + metric.ActualQty;
            }
        }
    }

    public bool AddCompletedSession(string machine, string orderId, string sessionId, long actualQty)
    {
        lock (_lock)
        {
            var sessionKey = SessionKey(machine, orderId, sessionId);
            if (!_appliedSessionKeys.Add(sessionKey))
            {
                return false;
            }

            var orderKey = OrderKey(machine, orderId);
            _completedQtyByOrder[orderKey] = _completedQtyByOrder.GetValueOrDefault(orderKey) + actualQty;
            return true;
        }
    }

    private static bool IsFinalSessionMetric(ProductionMetric metric) =>
        metric.IsFinal &&
        metric.BucketType.Equals("session", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(metric.SessionId);

    private static string OrderKey(string machine, string orderId) => $"{machine.Trim()}|{orderId.Trim()}";

    private static string SessionKey(string machine, string orderId, string sessionId) =>
        $"{OrderKey(machine, orderId)}|{sessionId.Trim()}";
}
