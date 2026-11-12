namespace MvcApp.Models;

/// <summary>The course's domain payload. In level 1 it is only passed in and greeted.</summary>
public record Order(
    string OrderId,      // Guid as string
    string CustomerId,   // Guid as string
    string Product,
    decimal Amount,
    string Status        // "created", "approved", "needs_review"
);
