namespace Core.Application.Entities;

public sealed class Customer
{
    public Guid   Id        { get; init; } = Guid.NewGuid();
    public string FirstName { get; set; } = string.Empty;
    public string LastName  { get; set; } = string.Empty;
    public string Email     { get; set; } = string.Empty;
    public string Phone     { get; set; } = string.Empty;
    public bool   IsActive  { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}
