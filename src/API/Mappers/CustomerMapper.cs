using Core.Application.Entities;

namespace API.Mappers;

public sealed record CustomerResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    string Phone,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public static class CustomerMapper
{
    public static CustomerResponse ToResponse(this Customer customer) =>
        new(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.FullName,
            customer.Email,
            customer.Phone,
            customer.IsActive,
            customer.CreatedAt,
            customer.UpdatedAt);

    public static IReadOnlyList<CustomerResponse> ToResponseList(
        this IEnumerable<Customer> customers) =>
        customers.Select(c => c.ToResponse()).ToList();
}
