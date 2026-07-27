using Microsoft.AspNetCore.SignalR;

namespace ArchIntel.Api.Realtime;

/// <summary>`/hubs/architecture` (05-rest-api.md Section 5.1). Phase 3 is single-repo, no auth —
/// every client implicitly belongs to one global group, so there are no server-to-client RPC
/// methods to expose yet (JoinRepo/LeaveRepo are a Phase 4 addition once multi-repo scoping
/// exists). All traffic here is server -> client, pushed via IArchitectureChangeNotifier.</summary>
public sealed class ArchitectureHub : Hub;
