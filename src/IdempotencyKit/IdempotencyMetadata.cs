namespace IdempotencyKit;

public sealed class IdempotencyMetadata
{
    private IdempotencyMetadata()
    {
    }

    public static IdempotencyMetadata Instance { get; } = new();
}
