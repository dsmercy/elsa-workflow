using Elsa.Workflows.Management;
using Elsa.Workflows.Management.Filters;
using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>
/// Reads this level's workflow instances straight from Elsa's instance store — the same data
/// Studio shows under Workflows → Instances. No separate "orders" table is needed for the demo:
/// the order ID is the instance's CORRELATION ID and the status is the workflow's "Status" output.
/// </summary>
public static class OrderInstances
{
    public static Dictionary<string, ApprovalPattern> DefinitionIds(IConfiguration configuration) =>
        Enum.GetValues<ApprovalPattern>()
            .Select(p => (Pattern: p, Id: configuration[ApprovalPatterns.ConfigKey(p)]))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !x.Id!.StartsWith('<'))
            .GroupBy(x => x.Id!)
            .ToDictionary(g => g.Key, g => g.First().Pattern);

    public static async Task<List<OrderInstanceRow>> LoadAsync(
        IWorkflowInstanceStore store, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var definitions = DefinitionIds(configuration);
        if (definitions.Count == 0)
            return [];

        var instances = await store.FindManyAsync(new WorkflowInstanceFilter
        {
            DefinitionIds = definitions.Keys.ToList()
        }, cancellationToken);

        return instances
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .Select(i =>
            {
                object? status = null;
                i.WorkflowState?.Output?.TryGetValue("Status", out status);
                return new OrderInstanceRow(
                    WorkflowInstanceId: i.Id,
                    DefinitionId: i.DefinitionId,
                    Pattern: definitions.TryGetValue(i.DefinitionId, out var p) ? p : null,
                    OrderId: i.CorrelationId,
                    Name: i.Name,
                    Status: i.Status.ToString(),
                    SubStatus: i.SubStatus.ToString(),
                    OrderStatus: status?.ToString(),
                    CreatedAt: i.CreatedAt);
            })
            .ToList();
    }
}
