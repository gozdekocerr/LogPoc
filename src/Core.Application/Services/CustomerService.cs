using Core.Application.Entities;
using Core.Application.Exceptions;
using Core.Application.Repositories;
using Microsoft.Extensions.Logging;

namespace Core.Application.Services;

public sealed class CustomerService : ICustomerService
{
    private readonly ICustomerRepository        _repository;
    private readonly ILogger<CustomerService>   _logger;

    public CustomerService(ICustomerRepository repository, ILogger<CustomerService> logger)
    {
        _repository = repository;
        _logger     = logger;
    }
    public async Task<IReadOnlyList<Customer>> GetAllAsync(CancellationToken ct = default)
    {
        var customers = await _repository.GetAllAsync(ct);

        _logger.LogInformation("Tüm müşteriler listelendi. Toplam kayıt: {Count}", customers.Count);

        return customers;
    }
    public async Task<IReadOnlyList<Customer>> GetActiveAsync(CancellationToken ct = default)
    {
        var customers = await _repository.GetActiveAsync(ct);

        _logger.LogInformation("Aktif müşteriler listelendi. Toplam aktif: {Count}", customers.Count);

        return customers;
    }
    public async Task<Customer> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _repository.GetByIdAsync(id, ct);

        if (customer is null)
        {
            _logger.LogWarning(
                "Müşteri bulunamadı. Id={CustomerId} — Kayıt sistemde mevcut değil.", id);

            throw new NotFoundException($"Id={id} ile eşleşen müşteri bulunamadı.");
        }

        _logger.LogInformation(
            "Müşteri getirildi. Id={CustomerId} | Ad={FullName}",
            customer.Id, customer.FullName);

        return customer;
    }
    public async Task<Customer> CreateAsync(
        string firstName, string lastName, string email, string phone,
        CancellationToken ct = default)
    {
        var validationErrors = Validate(firstName, lastName, email, phone);
        if (validationErrors.Count > 0)
        {
            _logger.LogError(
                "Müşteri oluşturma başarısız — Validasyon hatası. Hatalar: [{Errors}] | Email={Email}",
                string.Join(", ", validationErrors), email);

            throw new ValidationException(validationErrors);
        }

        var existing = await _repository.GetByEmailAsync(email, ct);
        if (existing is not null)
        {
            _logger.LogWarning(
                "Duplicate e-posta tespit edildi. Email={Email} zaten Id={ExistingId} müşterisine ait. Yeni kayıt oluşturulmadı.",
                email, existing.Id);

            throw new DuplicateEmailException(email, existing.Id);
        }

        var customer = new Customer
        {
            FirstName = firstName.Trim(),
            LastName  = lastName.Trim(),
            Email     = email.Trim().ToLowerInvariant(),
            Phone     = phone.Trim()
        };

        await _repository.AddAsync(customer, ct);

        _logger.LogInformation(
            "Yeni müşteri oluşturuldu. Id={CustomerId} | Ad={FullName} | Email={Email}",
            customer.Id, customer.FullName, customer.Email);

        return customer;
    }
    public async Task<Customer> UpdateAsync(
        Guid id, string firstName, string lastName, string phone,
        CancellationToken ct = default)
    {
        var customer = await _repository.GetByIdAsync(id, ct);

        if (customer is null)
        {
            _logger.LogWarning(
                "Güncelleme başarısız — Müşteri bulunamadı. Id={CustomerId}", id);

            throw new NotFoundException($"Id={id} ile eşleşen müşteri bulunamadı.");
        }

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            _logger.LogError(
                "Güncelleme başarısız — Ad veya soyad boş gönderilemez. Id={CustomerId}", id);

            throw new ValidationException(["Ad ve soyad zorunludur."]);
        }

        var oldName = customer.FullName;
        customer.FirstName = firstName.Trim();
        customer.LastName  = lastName.Trim();
        customer.Phone     = phone.Trim();
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.UpdateAsync(customer, ct);

        _logger.LogInformation(
            "Müşteri güncellendi. Id={CustomerId} | Eski Ad={OldName} → Yeni Ad={NewName}",
            customer.Id, oldName, customer.FullName);

        return customer;
    }
    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _repository.GetByIdAsync(id, ct);

        if (customer is null)
        {
            _logger.LogWarning(
                "Pasife alma başarısız — Müşteri bulunamadı. Id={CustomerId}", id);

            throw new NotFoundException($"Id={id} ile eşleşen müşteri bulunamadı.");
        }

        if (!customer.IsActive)
        {
            _logger.LogWarning(
                "Müşteri zaten pasif durumda. Id={CustomerId} | Ad={FullName} — İşlem atlandı.",
                customer.Id, customer.FullName);

            return;
        }

        customer.IsActive  = false;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.UpdateAsync(customer, ct);

        _logger.LogInformation(
            "Müşteri pasife alındı. Id={CustomerId} | Ad={FullName}",
            customer.Id, customer.FullName);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var customer = await _repository.GetByIdAsync(id, ct);

        if (customer is null)
        {
            _logger.LogWarning(
                "Silme başarısız — Müşteri bulunamadı. Id={CustomerId}", id);

            throw new NotFoundException($"Id={id} ile eşleşen müşteri bulunamadı.");
        }

        if (customer.IsActive)
        {
            _logger.LogError(
                "Silme reddedildi — Aktif müşteri silinemez. Id={CustomerId} | Ad={FullName} — Önce pasife alın.",
                customer.Id, customer.FullName);

            throw new BusinessRuleException(
                $"Aktif müşteri ({customer.FullName}) silinemez. Önce pasife alınmalıdır.");
        }

        await _repository.DeleteAsync(id, ct);

        _logger.LogInformation(
            "Müşteri silindi. Id={CustomerId} | Ad={FullName}",
            customer.Id, customer.FullName);
    }

    private static List<string> Validate(string firstName, string lastName, string email, string phone)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(firstName))
            errors.Add("Ad zorunludur.");

        if (string.IsNullOrWhiteSpace(lastName))
            errors.Add("Soyad zorunludur.");

        if (string.IsNullOrWhiteSpace(email))
            errors.Add("E-posta zorunludur.");
        else if (!email.Contains('@') || !email.Contains('.'))
            errors.Add($"E-posta formatı geçersiz: '{email}'.");

        if (string.IsNullOrWhiteSpace(phone))
            errors.Add("Telefon numarası zorunludur.");

        return errors;
    }
}
