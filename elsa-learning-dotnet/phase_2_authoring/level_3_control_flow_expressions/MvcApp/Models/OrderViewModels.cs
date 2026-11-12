using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models;

/// <summary>One row of the form. Rows left blank are ignored.</summary>
public class OrderLineForm
{
    [StringLength(100)]
    public string? Product { get; set; }

    [Range(0, 10_000)]
    public int Quantity { get; set; }

    [Range(0, 1_000_000)]
    public decimal UnitPrice { get; set; }

    public bool IsBlank => string.IsNullOrWhiteSpace(Product);
}

/// <summary>What the user types in the Orders/Create form: up to three lines.</summary>
public class OrderForm
{
    public List<OrderLineForm> Lines { get; set; } =
    [
        new() { Product = "Office chair", Quantity = 2, UnitPrice = 180m },
        new() { Product = "Desk lamp", Quantity = 4, UnitPrice = 35m },
        new()
    ];
}

/// <summary>The workflow's outputs plus run metadata.</summary>
public class OrderResult
{
    public required Order Order { get; init; }
    public required string WorkflowInstanceId { get; init; }
    public required string WorkflowStatus { get; init; }
    public required string WorkflowSubStatus { get; init; }
    public decimal? Total { get; init; }
    public string? OrderStatus { get; init; }
    public string? Tier { get; init; }
    public string? Message { get; init; }
}

public class CreateOrderViewModel
{
    public OrderForm Form { get; set; } = new();
    public OrderResult? Result { get; set; }
    public string? Error { get; set; }
}
