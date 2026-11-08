namespace Graspea.Flint.Delivery;

/// <summary>The version of the data delivery contract: the manifest, page and error documents a host writes and the
/// browser reads. Every document carries it as <c>contract</c>.</summary>
public static class DeliveryContract
{
    /// <summary>The contract version written into every manifest, page and error document.</summary>
    public const int Version = 1;
}
