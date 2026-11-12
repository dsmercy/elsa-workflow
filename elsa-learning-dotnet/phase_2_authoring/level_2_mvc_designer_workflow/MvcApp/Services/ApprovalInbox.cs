using System.Collections.Concurrent;
using MvcApp.Models;

namespace MvcApp.Services;

/// <summary>
/// LEARNING-ONLY in-memory inbox for (a) notifications the workflows POST to the webhook and
/// (b) pending "ReviewOrder" Run Tasks. It is cleared on restart. The workflow instances themselves
/// (and their bookmarks) are persisted by Elsa in SQLite — a real app would store tasks in its own DB.
/// </summary>
public class ApprovalInbox
{
    private const int MaxNotifications = 200;

    private readonly ConcurrentQueue<WebhookNotification> _notifications = new();
    private readonly ConcurrentDictionary<string, PendingReviewTask> _tasks = new();

    public void AddNotification(WebhookNotification notification)
    {
        _notifications.Enqueue(notification);
        while (_notifications.Count > MaxNotifications && _notifications.TryDequeue(out _)) { }
    }

    public IReadOnlyList<WebhookNotification> GetNotifications() =>
        _notifications.Reverse().ToList();

    public void AddTask(PendingReviewTask task) => _tasks[task.TaskId] = task;

    public bool TryRemoveTask(string taskId, out PendingReviewTask? task)
    {
        var removed = _tasks.TryRemove(taskId, out var found);
        task = found;
        return removed;
    }

    public IReadOnlyList<PendingReviewTask> GetTasks() =>
        _tasks.Values.OrderBy(t => t.RequestedAt).ToList();
}
