namespace Portal.Domain.Entities;

public enum OrganizationRole
{
    /// <summary>Agency owner: full control of the organization.</summary>
    Owner = 1,
    /// <summary>Agency user: manages projects and their resources.</summary>
    Member = 2,
    /// <summary>Client user: limited, view-mostly access.</summary>
    Client = 3,
}

public class OrganizationMember
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public User? User { get; set; }
}
