using API.Mappers;
using Core.Application.Exceptions;
using Core.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

public sealed record CreateCustomerRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone);

public sealed record UpdateCustomerRequest(
    string FirstName,
    string LastName,
    string Phone);

[ApiController]
[Route("api/[controller]")]
public sealed class CustomerController : ControllerBase
{
    private readonly ICustomerService _service;

    public CustomerController(ICustomerService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var customers = await _service.GetAllAsync(ct);
        return Ok(customers.ToResponseList());
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var customers = await _service.GetActiveAsync(ct);
        return Ok(customers.ToResponseList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        try
        {
            var customer = await _service.GetByIdAsync(id, ct);
            return Ok(customer.ToResponse());
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        try
        {
            var customer = await _service.CreateAsync(
                request.FirstName, request.LastName,
                request.Email,     request.Phone, ct);

            return CreatedAtAction(nameof(GetById),
                new { id = customer.Id },
                customer.ToResponse());
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { Errors = ex.Errors });
        }
        catch (DuplicateEmailException ex)
        {
            return Conflict(new { ex.Message, ex.Email, ex.ExistingId });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        try
        {
            var customer = await _service.UpdateAsync(
                id, request.FirstName, request.LastName, request.Phone, ct);

            return Ok(customer.ToResponse());
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { ex.Message });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { Errors = ex.Errors });
        }
    }

    [HttpPatch("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.DeactivateAsync(id, ct);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return UnprocessableEntity(new { ex.Message });
        }
    }
}
