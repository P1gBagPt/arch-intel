using System.Collections.Concurrent;

namespace ArchIntel.Api.Auth;

public sealed class InvitationStore
{
    private readonly ConcurrentDictionary<string, Invitation> _invitations = new();

    public Invitation Create(string repoId, string email, RepoRole role)
    {
        var invitation = new Invitation { InvitationId = $"inv_{Guid.NewGuid():N}"[..12], RepoId = repoId, Email = email, Role = role };
        _invitations[invitation.InvitationId] = invitation;
        return invitation;
    }

    public Invitation? Get(string invitationId) => _invitations.GetValueOrDefault(invitationId);

    public IReadOnlyList<Invitation> ListForRepo(string repoId) => _invitations.Values.Where(i => i.RepoId == repoId).ToList();

    public void Update(Invitation invitation) => _invitations[invitation.InvitationId] = invitation;
}
