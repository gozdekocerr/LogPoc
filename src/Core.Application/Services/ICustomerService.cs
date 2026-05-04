using Core.Application.Entities;

namespace Core.Application.Services;

public interface ICustomerService
{
    Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken ct = default);
    Task<Customer>  GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Customer>  CreateAsync(string firstName, string lastName, string email, string phone, CancellationToken ct = default);
    Task<Customer>  UpdateAsync(Guid id, string firstName, string lastName, string phone, CancellationToken ct = default);
    Task            DeactivateAsync(Guid id, CancellationToken ct = default);
    Task            DeleteAsync(Guid id, CancellationToken ct = default);
}
