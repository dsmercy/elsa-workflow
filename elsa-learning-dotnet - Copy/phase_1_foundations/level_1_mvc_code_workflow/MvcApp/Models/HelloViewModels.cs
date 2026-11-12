using System.ComponentModel.DataAnnotations;

namespace MvcApp.Models;

/// <summary>What the user types in the form.</summary>
public class OrderForm
{
    [Required, StringLength(100)]
    public string Product { get; set; } = "Coffee beans";

    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; } = 42.50m;
}

/// <summary>What the view shows after the workflow ran.</summary>
public class HelloRunResult
{
    public required Order Order { get; init; }
    public required string WorkflowInstanceId { get; init; }
    public required string Status { get; init; }
    public required string SubStatus { get; init; }
    public string? Message { get; init; }
}

public class HelloPageViewModel
{
    public OrderForm Form { get; set; } = new();
    public HelloRunResult? Result { get; set; }
    public string? Error { get; set; }
}
