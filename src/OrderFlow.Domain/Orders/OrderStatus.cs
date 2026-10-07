namespace OrderFlow.Domain.Orders;

public enum OrderStatus
{
    Pending = 0,
    StockReserved = 1,
    Paid = 2,
    Confirmed = 3,
    Cancelled = 4
}
