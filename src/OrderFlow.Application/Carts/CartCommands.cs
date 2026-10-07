using FluentValidation;
using MediatR;
using OrderFlow.Application.Abstractions;
using OrderFlow.Application.Common;
using OrderFlow.Domain.Carts;

namespace OrderFlow.Application.Carts;

public sealed record GetCartQuery : IRequest<CartDto>;

internal sealed class GetCartQueryHandler(ICartRepository carts, ICurrentUser currentUser) : IRequestHandler<GetCartQuery, CartDto>
{
    public async Task<CartDto> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        var cart = await carts.GetByCustomerAsync(currentUser.CustomerId, cancellationToken);
        return cart is null ? CartDto.Empty : CartDto.From(cart);
    }
}

public sealed record AddCartItemCommand(Guid ProductId, int Quantity) : IRequest<CartDto>;

internal sealed class AddCartItemCommandValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.Quantity).InclusiveBetween(1, 100);
    }
}

internal sealed class AddCartItemCommandHandler(
    ICartRepository carts,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser) : IRequestHandler<AddCartItemCommand, CartDto>
{
    public async Task<CartDto> Handle(AddCartItemCommand request, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdAsync(request.ProductId, cancellationToken)
                      ?? throw new NotFoundException($"Product {request.ProductId} was not found.");

        var customerId = currentUser.CustomerId;

        // Two quick requests (a double click) can race to create the cart or to update the same item.
        return await ConflictRetry.RunAsync(unitOfWork, async () =>
        {
            var cart = await carts.GetByCustomerAsync(customerId, cancellationToken);
            if (cart is null)
            {
                cart = Cart.CreateFor(customerId);
                carts.Add(cart);
            }

            cart.AddItem(product.Id, request.Quantity, product.Price);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CartDto.From(cart);
        });
    }
}

public sealed record RemoveCartItemCommand(Guid ProductId) : IRequest<CartDto>;

internal sealed class RemoveCartItemCommandHandler(ICartRepository carts, IUnitOfWork unitOfWork, ICurrentUser currentUser)
    : IRequestHandler<RemoveCartItemCommand, CartDto>
{
    public async Task<CartDto> Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        var customerId = currentUser.CustomerId;

        return await ConflictRetry.RunAsync(unitOfWork, async () =>
        {
            var cart = await carts.GetByCustomerAsync(customerId, cancellationToken)
                       ?? throw new NotFoundException("The cart is empty.");

            cart.RemoveItem(request.ProductId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CartDto.From(cart);
        });
    }
}
