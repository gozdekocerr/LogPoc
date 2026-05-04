using System.Collections.Concurrent;
using Core.Application.Entities;
using Core.Application.Repositories;

namespace Infrastructure.Persistence.Repositories;

internal sealed class InMemoryCustomerRepository : ICustomerRepository
{
    private readonly ConcurrentDictionary<Guid, Customer> _store = new();

    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var customer);
        return Task.FromResult(customer);
    }

    public Task<Customer?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var customer = _store.Values
            .FirstOrDefault(c => string.Equals(c.Email, email.Trim().ToLowerInvariant(),
                                               StringComparison.Ordinal));
        return Task.FromResult(customer);
    }

    public Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Customer> result = _store.Values
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken ct = default)
    {
        IReadOnlyList<Customer> result = _store.Values
            .Where(c => c.IsActive)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToList();
        return Task.FromResult(result);
    }

    public Task AddAsync(Customer customer, CancellationToken ct = default)
    {
        _store[customer.Id] = customer;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Customer customer, CancellationToken ct = default)
    {
        _store[customer.Id] = customer;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var removed = _store.TryRemove(id, out _);
        return Task.FromResult(removed);
    }

}
