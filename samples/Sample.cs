namespace Acme.Billing;

/// <summary>A customer invoice — sample input for codewalker.</summary>
public sealed class Invoice
{
    public int Number { get; init; }
    public decimal Total { get; set; }

    public void AddLine(string description, decimal amount) { }

    /// <summary>A nested type, to show the walker's nesting-aware naming.</summary>
    public enum Status { Draft, Sent, Paid }
}

public interface IInvoiceStore
{
    Invoice? Find(int number);
    void Save(Invoice invoice);
}

public readonly record struct Money(decimal Amount, string Currency);
