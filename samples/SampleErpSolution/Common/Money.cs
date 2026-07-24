namespace SampleErp.Common;

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException("Currency mismatch.");
        }

        return new Money(Amount + other.Amount, Currency);
    }
}
