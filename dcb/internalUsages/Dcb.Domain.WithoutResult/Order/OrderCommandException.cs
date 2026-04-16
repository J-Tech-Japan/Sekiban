namespace Dcb.Domain.WithoutResult.Order;

public sealed class OrderCommandException(string message) : InvalidOperationException(message);
