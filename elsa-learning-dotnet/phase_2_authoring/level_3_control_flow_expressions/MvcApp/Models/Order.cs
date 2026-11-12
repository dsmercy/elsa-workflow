namespace MvcApp.Models;

/// <summary>One line of an order. The workflow's ForEach loops over these.</summary>
public record OrderLine(string Product, int Quantity, decimal UnitPrice);

/// <summary>
/// The course's domain payload, extended in level 3 with <see cref="Lines"/>.
/// Amount starts at 0: the workflow computes the total from the lines and returns it as output.
/// </summary>
public record Order(
    string OrderId,      // Guid as string
    string CustomerId,   // Guid as string
    string Product,      // summary of the line products
    decimal Amount,
    string Status,       // "created", "approved", "needs_review"
    List<OrderLine> Lines
);
