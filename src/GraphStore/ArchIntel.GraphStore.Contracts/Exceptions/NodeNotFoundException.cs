namespace ArchIntel.GraphStore.Contracts.Exceptions;

public sealed class NodeNotFoundException : Exception
{
    public string NodeId { get; }

    public NodeNotFoundException(string nodeId)
        : base($"No node found with id '{nodeId}'.")
    {
        NodeId = nodeId;
    }
}
