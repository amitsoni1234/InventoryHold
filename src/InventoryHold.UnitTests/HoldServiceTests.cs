using InventoryHold.Domain;
using InventoryHold.Domain.Commands;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Services;
using InventoryHold.UnitTests.Fakes;

namespace InventoryHold.UnitTests;

public class HoldServiceTests
{
    private readonly FakeStore _store = new();
    private readonly FakeCache _cache = new();
    private readonly FakePublisher _publisher = new();
    private readonly FixedClock _clock = new();
    private readonly HoldService _service;

    public HoldServiceTests()
    {
        _store.AddProduct("mouse", "Wireless Mouse", 10);
        _store.AddProduct("keyboard", "Mechanical Keyboard", 4);
        _service = new HoldService(_store, _cache, _publisher, _clock, new FixedDuration());
    }

    [Fact]
    public async Task Create_RejectsEmptyItems()
    {
        var result = await _service.CreateAsync(new CreateHoldCommand([], null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.Validation, result.Error!.Code);
        Assert.Equal(400, result.Error.StatusCode);
        Assert.Empty(_publisher.Created);
        Assert.Equal(0, _cache.Invalidations);
    }

    [Fact]
    public async Task Create_RejectsNonPositiveQuantity()
    {
        var result = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 0)], null),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.Validation, result.Error!.Code);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
    }

    [Fact]
    public async Task Create_RejectsBlankProductId()
    {
        var result = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("  ", 1)], null),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.Validation, result.Error!.Code);
    }

    [Fact]
    public async Task Create_RejectsDuplicateProductIds()
    {
        var result = await _service.CreateAsync(
            new CreateHoldCommand(
                [new CreateHoldLine("mouse", 1), new CreateHoldLine(" mouse ", 2)],
                null),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.DuplicateProduct, result.Error!.Code);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
        Assert.Empty(_publisher.Created);
    }

    [Fact]
    public async Task Create_ReturnsNotFound_WhenProductMissing()
    {
        var result = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("missing", 1)], null),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.ProductNotFound, result.Error!.Code);
        Assert.Equal(404, result.Error.StatusCode);
        Assert.Empty(_publisher.Created);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenStockInsufficient()
    {
        var result = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("keyboard", 5)], null),
            CancellationToken.None);

        Assert.Equal(ErrorCodes.InsufficientStock, result.Error!.Code);
        Assert.Equal(409, result.Error.StatusCode);
        Assert.Equal(4, _store.Products["keyboard"].AvailableQuantity);
        Assert.Empty(_publisher.Created);
        Assert.Equal(0, _cache.Invalidations);
    }

    [Fact]
    public async Task Create_PlacesHold_DeductsEveryLine_PublishesAndInvalidatesCache()
    {
        _cache.ThrowOnRead = true;
        var result = await _service.CreateAsync(
            new CreateHoldCommand(
                [new CreateHoldLine("mouse", 2), new CreateHoldLine("keyboard", 1)],
                "req-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HoldStatus.Active, result.Value!.Status);
        Assert.Equal(_clock.UtcNow.AddMinutes(15), result.Value.ExpiresAtUtc);
        Assert.Equal(8, _store.Products["mouse"].AvailableQuantity);
        Assert.Equal(3, _store.Products["keyboard"].AvailableQuantity);
        Assert.Equal("Wireless Mouse", result.Value.Items.Single(item => item.ProductId == "mouse").ProductName);
        Assert.Single(_publisher.Created);
        Assert.Equal(result.Value.HoldId, _publisher.Created[0].HoldId);
        Assert.Equal(1, _cache.Invalidations);
        Assert.Empty(_publisher.Released);
    }

    [Fact]
    public async Task Create_SameClientRequest_DoesNotDeductOrPublishAgain()
    {
        var command = new CreateHoldCommand([new CreateHoldLine("mouse", 2)], "same-key");
        var first = await _service.CreateAsync(command, CancellationToken.None);
        var second = await _service.CreateAsync(command, CancellationToken.None);

        Assert.Equal(first.Value!.HoldId, second.Value!.HoldId);
        Assert.Equal(8, _store.Products["mouse"].AvailableQuantity);
        Assert.Single(_publisher.Created);
        Assert.Equal(1, _cache.Invalidations);
    }

    [Fact]
    public async Task Release_RestoresStock_PublishesAndInvalidatesCache()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 3)], null),
            CancellationToken.None);

        var released = await _service.ReleaseAsync(created.Value!.HoldId, CancellationToken.None);

        Assert.True(released.IsSuccess);
        Assert.Equal(HoldStatus.Released, released.Value!.Status);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
        Assert.Single(_publisher.Released);
        Assert.Equal(2, _cache.Invalidations);
    }

    [Fact]
    public async Task Release_MissingHold_Returns404()
    {
        var result = await _service.ReleaseAsync("missing", CancellationToken.None);

        Assert.Equal(ErrorCodes.HoldNotFound, result.Error!.Code);
        Assert.Equal(404, result.Error.StatusCode);
        Assert.Empty(_publisher.Released);
    }

    [Fact]
    public async Task Release_AlreadyReleased_DoesNotRestoreAgain()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 3)], null),
            CancellationToken.None);
        await _service.ReleaseAsync(created.Value!.HoldId, CancellationToken.None);

        var again = await _service.ReleaseAsync(created.Value.HoldId, CancellationToken.None);

        Assert.Equal(ErrorCodes.HoldAlreadyReleased, again.Error!.Code);
        Assert.Equal(409, again.Error.StatusCode);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
        Assert.Single(_publisher.Released);
    }

    [Fact]
    public async Task Release_AlreadyExpired_DoesNotRestoreAgain()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 2)], null),
            CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(16);
        await _service.GetAsync(created.Value!.HoldId, CancellationToken.None);
        var stockAfterExpiry = _store.Products["mouse"].AvailableQuantity;

        var release = await _service.ReleaseAsync(created.Value.HoldId, CancellationToken.None);

        Assert.Equal(ErrorCodes.HoldExpired, release.Error!.Code);
        Assert.Equal(stockAfterExpiry, _store.Products["mouse"].AvailableQuantity);
        Assert.Single(_publisher.Expired);
        Assert.Empty(_publisher.Released);
    }

    [Fact]
    public async Task Get_MissingHold_Returns404()
    {
        var result = await _service.GetAsync("nope", CancellationToken.None);

        Assert.Equal(404, result.Error!.StatusCode);
    }

    [Fact]
    public async Task Get_ActiveHoldPastExpiry_ExpiresOnceAndRestoresStock()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("keyboard", 2)], null),
            CancellationToken.None);
        _clock.UtcNow = created.Value!.ExpiresAtUtc;

        var fetched = await _service.GetAsync(created.Value.HoldId, CancellationToken.None);

        Assert.True(fetched.IsSuccess);
        Assert.Equal(HoldStatus.Expired, fetched.Value!.Status);
        Assert.Equal(4, _store.Products["keyboard"].AvailableQuantity);
        Assert.Single(_publisher.Expired);
    }

    [Fact]
    public async Task Get_WhenCompareAndSetLoses_DoesNotRestoreOrPublishAgain()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 2)], null),
            CancellationToken.None);
        _store.Products["mouse"] = new Product("mouse", "Wireless Mouse", 10);
        _store.LoseNextTransition = true;
        _clock.UtcNow = created.Value!.ExpiresAtUtc.AddMinutes(1);

        var fetched = await _service.GetAsync(created.Value.HoldId, CancellationToken.None);

        Assert.True(fetched.IsSuccess);
        Assert.Equal(HoldStatus.Expired, fetched.Value!.Status);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
        Assert.Empty(_publisher.Expired);
    }

    [Fact]
    public async Task ConcurrentRelease_RestoresAndPublishesOnce()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 4)], null),
            CancellationToken.None);

        var first = _service.ReleaseAsync(created.Value!.HoldId, CancellationToken.None);
        var second = _service.ReleaseAsync(created.Value.HoldId, CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, results.Count(result => result.IsSuccess));
        Assert.Equal(1, results.Count(result => result.Error?.Code == ErrorCodes.HoldAlreadyReleased));
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
        Assert.Single(_publisher.Released);
        Assert.Equal(2, _cache.Invalidations);
    }

    [Fact]
    public async Task DeleteOfDueHold_ExpiresInsteadOfReleasing()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 1)], null),
            CancellationToken.None);
        _clock.UtcNow = created.Value!.ExpiresAtUtc.AddSeconds(1);

        var result = await _service.ReleaseAsync(created.Value.HoldId, CancellationToken.None);

        Assert.Equal(ErrorCodes.HoldExpired, result.Error!.Code);
        Assert.Equal(HoldStatus.Expired, _store.Holds[created.Value.HoldId].Status);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
        Assert.Single(_publisher.Expired);
        Assert.Empty(_publisher.Released);
    }

    [Fact]
    public async Task ExpireDueHolds_PublishesOnlyForTheWinner()
    {
        var created = await _service.CreateAsync(
            new CreateHoldCommand([new CreateHoldLine("mouse", 1)], null),
            CancellationToken.None);
        _clock.UtcNow = created.Value!.ExpiresAtUtc.AddMinutes(1);

        await _service.ExpireDueHoldsAsync(CancellationToken.None);
        await _service.ExpireDueHoldsAsync(CancellationToken.None);

        Assert.Single(_publisher.Expired);
        Assert.Equal(10, _store.Products["mouse"].AvailableQuantity);
    }

    [Fact]
    public async Task ListActive_DropsExpiredHoldsReturnedByTheStore()
    {
        var active = new Hold(
            "active",
            HoldStatus.Active,
            _clock.UtcNow,
            _clock.UtcNow.AddMinutes(5),
            [new HoldItem("mouse", "Wireless Mouse", 1)],
            null);
        var stale = new Hold(
            "stale",
            HoldStatus.Active,
            _clock.UtcNow.AddMinutes(-20),
            _clock.UtcNow.AddMinutes(-1),
            [new HoldItem("mouse", "Wireless Mouse", 1)],
            null);
        _store.ActiveOverride = [active, stale];

        var listed = await _service.ListActiveAsync(CancellationToken.None);

        Assert.Equal(["active"], listed.Select(hold => hold.HoldId).ToArray());
    }
}
