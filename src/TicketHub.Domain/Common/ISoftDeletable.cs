namespace TicketHub.Domain.Common;

/// <summary>
/// Entities that are never physically deleted (BR-08). Deletion flips <see cref="IsDeleted"/> and
/// records when it happened; a global query filter hides them from normal queries.
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTime? DeletedAtUtc { get; }

    void MarkDeleted(DateTime utcNow);
}
