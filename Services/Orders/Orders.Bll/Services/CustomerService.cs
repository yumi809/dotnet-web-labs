using AutoMapper;
using Microsoft.Extensions.Logging;
using Orders.Bll.Dto;
using Orders.Dal.Interfaces;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;

namespace Orders.Bll.Services;

public interface ICustomerService
{
    Task<CustomerDto> GetAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct);
    Task<CustomerDto> CreateAsync(CreateCustomerDto dto, string user, CancellationToken ct);
    Task<CustomerStatsDto> GetStatsAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<ProductInfoDto>> GetProductDirectoryAsync(CancellationToken ct);
}

public sealed class CustomerService(IUnitOfWork uow, IMapper mapper, ILogger<CustomerService> logger) : ICustomerService
{
    public async Task<CustomerDto> GetAsync(long id, CancellationToken ct)
    {
        var customer = await uow.Customers.GetWithProfileAsync(id, ct) ?? throw new NotFoundException(nameof(Customer), id);
        return mapper.Map<CustomerDto>(customer);
    }

    public async Task<IReadOnlyList<CustomerDto>> GetAllAsync(CancellationToken ct) =>
        mapper.Map<IReadOnlyList<CustomerDto>>(await uow.Customers.GetAllAsync(ct));

    public async Task<CustomerDto> CreateAsync(CreateCustomerDto dto, string user, CancellationToken ct)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        if (await uow.Customers.EmailExistsAsync(email, ct))
            throw new BusinessConflictException($"Customer with email '{email}' already exists");

        var customer = mapper.Map<Customer>(dto);
        customer.CreatedBy = user;
        customer.Id = await uow.Customers.AddAsync(customer, ct);
        logger.LogInformation("Customer {CustomerId} registered", customer.Id);
        return await GetAsync(customer.Id, ct);
    }

    public async Task<CustomerStatsDto> GetStatsAsync(long id, CancellationToken ct)
    {
        var stats = await uow.Customers.GetOrderStatsAsync(id, ct) ?? throw new NotFoundException(nameof(Customer), id);
        return mapper.Map<CustomerStatsDto>(stats);
    }

    public async Task<IReadOnlyList<ProductInfoDto>> GetProductDirectoryAsync(CancellationToken ct) =>
        mapper.Map<IReadOnlyList<ProductInfoDto>>(await uow.Products.GetAllAsync(ct));
}
