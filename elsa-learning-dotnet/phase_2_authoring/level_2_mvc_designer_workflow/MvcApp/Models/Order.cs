namespace MvcApp.Models;

/// <summary>The course's domain payload, passed to the workflow as its "Order" input.</summary>
public record Order(
    string OrderId,      // Guid as string
    string CustomerId,   // Guid as string
    string Product,
    decimal Amount,
    string Status        // "created", "approved", "needs_review"
);
