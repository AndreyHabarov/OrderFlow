using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Contracts;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.Messaging;
using OrderFlow.Payments;
using OrderFlow.Payments.Data;

namespace OrderFlow.IntegrationTests.Services;

[Collection(ServicesTestGroup.Name)]
public sealed class PaymentsServiceTests(ServicesFixture fixture)
{
    private static readonly string SucceededKey = MessageCatalog.RoutingKeyOf<PaymentSucceeded>();
    private static readonly string FailedKey = MessageCatalog.RoutingKeyOf<PaymentFailed>();

    private async Task<TestHost> StartPaymentsAsync(Dictionary<string, string?>? overrides = null, Action<IServiceCollection>? extra = null)
    {
        await fixture.PurgeQueueAsync(Topology.Payments); // leftovers of an earlier test must not leak in
        await fixture.SetPaymentModeAsync(null);

        var settings = fixture.Settings;
        settings["Payments:TimeoutDelay"] = "00:00:00.300";
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = fixture.BuildHost(services =>
        {
            services.AddPaymentsService(configuration, options => options.RetryDelay = TimeSpan.FromMilliseconds(100));
            extra?.Invoke(services);
        });
        await host.StartAsync();
        return host;
    }

    private async Task<string> OutcomeQueueAsync()
    {
        var queue = RabbitMqFixture.NewQueue(SucceededKey, FailedKey);
        await fixture.DeclareBoundQueueAsync(queue.Name, SucceededKey);
        await fixture.DeclareBoundQueueAsync(queue.Name, FailedKey);
        return queue.Name;
    }

    private static StockReserved Reserved(decimal amount = 75m) => new(Guid.NewGuid(), Guid.NewGuid(), amount, "USD");

    [Fact]
    public async Task Charges_the_customer_by_default()
    {
        await using var payments = await StartPaymentsAsync();
        var outcomes = await OutcomeQueueAsync();
        var order = Reserved(75m);

        await payments.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order, MessageContext.ForNewFlow("flow-pay-1"));
        var succeeded = (await fixture.ReceiveAsync<PaymentSucceeded>(outcomes, 1)).Single();

        Assert.Equal(order.OrderId, succeeded.OrderId);
        Assert.Equal(75m, succeeded.Amount);
        Assert.Equal("USD", succeeded.Currency);
        await payments.StopAsync();
    }

    [Fact]
    public async Task A_declined_payment_is_reported_as_failed()
    {
        await using var payments = await StartPaymentsAsync();
        await fixture.SetPaymentModeAsync(PaymentEmulator.Decline);
        var outcomes = await OutcomeQueueAsync();
        var order = Reserved();

        await payments.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var failed = (await fixture.ReceiveAsync<PaymentFailed>(outcomes, 1)).Single();

        Assert.Equal(order.OrderId, failed.OrderId);
        Assert.Contains("declined", failed.Reason, StringComparison.OrdinalIgnoreCase);
        await payments.StopAsync();
    }

    [Fact]
    public async Task A_timeout_is_reported_as_failed_after_the_emulated_delay()
    {
        await using var payments = await StartPaymentsAsync();
        await fixture.SetPaymentModeAsync(PaymentEmulator.Timeout);
        var outcomes = await OutcomeQueueAsync();
        var order = Reserved();
        var stopwatch = Stopwatch.StartNew();

        await payments.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var failed = (await fixture.ReceiveAsync<PaymentFailed>(outcomes, 1)).Single();
        stopwatch.Stop();

        Assert.Equal(order.OrderId, failed.OrderId);
        Assert.Contains("timed out", failed.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(stopwatch.ElapsedMilliseconds >= 250, $"Answered after only {stopwatch.ElapsedMilliseconds} ms");
        await payments.StopAsync();
    }

    [Fact]
    public async Task The_mode_can_be_switched_while_the_service_is_running()
    {
        await using var payments = await StartPaymentsAsync();
        var outcomes = await OutcomeQueueAsync();
        var publisher = payments.Services.GetRequiredService<IMessagePublisher>();

        var results = new List<string>();
        foreach (var mode in new[] { PaymentEmulator.Success, PaymentEmulator.Decline, PaymentEmulator.Success })
        {
            await fixture.SetPaymentModeAsync(mode);
            await publisher.PublishAsync(Reserved());
            var answers = await fixture.ReceiveAsync<System.Text.Json.JsonElement>(outcomes, 1);
            results.Add(answers.Single().TryGetProperty("reason", out _) ? "failed" : "succeeded");
        }

        Assert.Equal(["succeeded", "failed", "succeeded"], results);
        await payments.StopAsync();
    }

    [Fact]
    public async Task A_duplicate_StockReserved_charges_once_and_repeats_the_answer()
    {
        await using var payments = await StartPaymentsAsync();
        var outcomes = await OutcomeQueueAsync();
        var order = Reserved();
        var publisher = payments.Services.GetRequiredService<IMessagePublisher>();

        await publisher.PublishAsync(order);
        await publisher.PublishAsync(order);
        var answers = await fixture.ReceiveAsync<PaymentSucceeded>(outcomes, 2);

        Assert.Equal(2, answers.Count);
        Assert.All(answers, answer => Assert.Equal(order.OrderId, answer.OrderId));
        await using var scope = payments.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.OrderId));
        await payments.StopAsync();
    }

    [Fact]
    public async Task A_lost_answer_is_recovered_by_redelivery_without_charging_twice()
    {
        await using var directPublisher = fixture.BuildHost();
        var failures = 1;
        await using var payments = await StartPaymentsAsync(extra: services => services.AddSingleton<IMessagePublisher>(
            new FailingOncePublisher(directPublisher.Services.GetRequiredService<IMessagePublisher>(), () => failures-- > 0)));
        var outcomes = await OutcomeQueueAsync();
        var order = Reserved();

        await directPublisher.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var succeeded = (await fixture.ReceiveAsync<PaymentSucceeded>(outcomes, 1, seconds: 30)).Single();

        Assert.Equal(order.OrderId, succeeded.OrderId);
        await using var scope = payments.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.OrderId == order.OrderId));
        await payments.StopAsync();
    }

    [Fact]
    public async Task Payments_keep_working_with_the_default_mode_when_Redis_is_unreachable()
    {
        await using var payments = await StartPaymentsAsync(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "127.0.0.1:1,abortConnect=false,connectTimeout=300,asyncTimeout=300,syncTimeout=300"
        });
        var outcomes = await OutcomeQueueAsync();
        var order = Reserved();

        await payments.Services.GetRequiredService<IMessagePublisher>().PublishAsync(order);
        var succeeded = (await fixture.ReceiveAsync<PaymentSucceeded>(outcomes, 1, seconds: 30)).Single();

        Assert.Equal(order.OrderId, succeeded.OrderId);
        await payments.StopAsync();
    }

    private sealed class FailingOncePublisher(IMessagePublisher inner, Func<bool> shouldFail) : IMessagePublisher
    {
        public Task<Guid> PublishAsync<T>(T message, MessageContext? cause = null, CancellationToken cancellationToken = default)
            where T : class =>
            shouldFail()
                ? throw new MessagePublishException("simulated broker failure")
                : inner.PublishAsync(message, cause, cancellationToken);
    }
}
