using InventoryHold.Domain;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using InventoryHold.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Mongo;

public sealed class MongoInventoryHoldStore : IInventoryHoldStore
{
    private readonly IMongoCollection<ProductDocument> _products;
    private readonly IMongoCollection<HoldDocument> _holds;
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoInventoryHoldStore> _logger;

    public MongoInventoryHoldStore(IMongoClient client, IOptions<MongoOptions> options, ILogger<MongoInventoryHoldStore> logger)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new InvalidOperationException("Mongo:ConnectionString is required.");
        }

        if (string.IsNullOrWhiteSpace(settings.DatabaseName))
        {
            throw new InvalidOperationException("Mongo:DatabaseName is required.");
        }

        _database = client.GetDatabase(settings.DatabaseName);
        _products = _database.GetCollection<ProductDocument>("products");
        _holds = _database.GetCollection<HoldDocument>("holds");
        _logger = logger;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        var admin = _database.Client.GetDatabase("admin");
        var started = DateTime.UtcNow;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var hello = await admin.RunCommandAsync<BsonDocument>(
                    new BsonDocument("hello", 1),
                    cancellationToken: cancellationToken);
                var primary = hello.TryGetValue("isWritablePrimary", out var value) && value.ToBoolean();
                if (primary)
                {
                    break;
                }
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                _logger.LogWarning(ex, "Waiting for MongoDB replica set primary.");
            }

            if (DateTime.UtcNow - started > TimeSpan.FromSeconds(90))
            {
                throw new InvalidOperationException(
                    "MongoDB did not become a writable replica-set primary within 90 seconds. Multi-document transactions require a replica set.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        var clientRequestIndex = new CreateIndexModel<HoldDocument>(
            Builders<HoldDocument>.IndexKeys.Ascending(hold => hold.ClientRequestId),
            new CreateIndexOptions<HoldDocument>
            {
                Name = "ux_client_request_id",
                Unique = true,
                Sparse = true
            });
        var expiryIndex = new CreateIndexModel<HoldDocument>(
            Builders<HoldDocument>.IndexKeys.Ascending(hold => hold.Status).Ascending(hold => hold.ExpiresAtUtc),
            new CreateIndexOptions { Name = "ix_status_expires" });
        await _holds.Indexes.CreateManyAsync([clientRequestIndex, expiryIndex], cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetAllProductsAsync(CancellationToken cancellationToken)
    {
        var documents = await _products.Find(FilterDefinition<ProductDocument>.Empty)
            .SortBy(product => product.Name)
            .ToListAsync(cancellationToken);
        return documents.Select(DocumentMapper.ToProduct).ToList();
    }

    public async Task<IReadOnlyList<Product>> GetProductsByIdsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        var filter = Builders<ProductDocument>.Filter.In(product => product.Id, productIds);
        var documents = await _products.Find(filter).ToListAsync(cancellationToken);
        return documents.Select(DocumentMapper.ToProduct).ToList();
    }

    public async Task SeedProductsIfEmptyAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        var count = await _products.CountDocumentsAsync(FilterDefinition<ProductDocument>.Empty, cancellationToken: cancellationToken);
        if (count > 0)
        {
            return;
        }

        if (products.Count == 0)
        {
            return;
        }

        await _products.InsertManyAsync(products.Select(DocumentMapper.ToDocument), cancellationToken: cancellationToken);
        _logger.LogInformation("Seeded {Count} inventory products.", products.Count);
    }

    public Task<PlaceHoldResult> PlaceHoldAsync(Hold hold, CancellationToken cancellationToken) =>
        WithTransactionRetryAsync(() => PlaceHoldOnceAsync(hold, cancellationToken), cancellationToken);

    public async Task<Hold?> GetHoldAsync(string holdId, CancellationToken cancellationToken)
    {
        var document = await _holds.Find(Builders<HoldDocument>.Filter.Eq(hold => hold.Id, holdId))
            .FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : DocumentMapper.ToHold(document);
    }

    public async Task<IReadOnlyList<Hold>> ListActiveUnexpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var filter = Builders<HoldDocument>.Filter.Eq(hold => hold.Status, nameof(HoldStatus.Active))
            & Builders<HoldDocument>.Filter.Gt(hold => hold.ExpiresAtUtc, utcNow);
        var documents = await _holds.Find(filter).SortByDescending(hold => hold.CreatedAtUtc).ToListAsync(cancellationToken);
        return documents.Select(DocumentMapper.ToHold).ToList();
    }

    public async Task<IReadOnlyList<Hold>> ListDueForExpiryAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var filter = Builders<HoldDocument>.Filter.Eq(hold => hold.Status, nameof(HoldStatus.Active))
            & Builders<HoldDocument>.Filter.Lte(hold => hold.ExpiresAtUtc, utcNow);
        var documents = await _holds.Find(filter).ToListAsync(cancellationToken);
        return documents.Select(DocumentMapper.ToHold).ToList();
    }

    public Task<TransitionResult> TryTransitionAndRestoreAsync(
        string holdId,
        HoldStatus targetStatus,
        CancellationToken cancellationToken) =>
        WithTransactionRetryAsync(
            () => TransitionOnceAsync(holdId, targetStatus, cancellationToken),
            cancellationToken);

    private async Task<PlaceHoldResult> PlaceHoldOnceAsync(Hold hold, CancellationToken cancellationToken)
    {
        using var session = await _database.Client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();
        try
        {
            if (hold.ClientRequestId is not null)
            {
                var existing = await _holds
                    .Find(session, Builders<HoldDocument>.Filter.Eq(document => document.ClientRequestId, hold.ClientRequestId))
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is not null)
                {
                    await AbortQuietlyAsync(session);
                    return new PlaceHoldResult(PlaceHoldStatus.AlreadyExists, DocumentMapper.ToHold(existing));
                }
            }

            foreach (var item in hold.Items.OrderBy(line => line.ProductId, StringComparer.Ordinal))
            {
                var filter = Builders<ProductDocument>.Filter.Eq(product => product.Id, item.ProductId)
                    & Builders<ProductDocument>.Filter.Gte(product => product.AvailableQuantity, item.Quantity);
                var update = Builders<ProductDocument>.Update.Inc(product => product.AvailableQuantity, -item.Quantity);
                var updated = await _products.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken);
                if (updated.ModifiedCount != 1)
                {
                    await AbortQuietlyAsync(session);
                    var exists = await _products
                        .Find(Builders<ProductDocument>.Filter.Eq(product => product.Id, item.ProductId))
                        .AnyAsync(cancellationToken);
                    return exists
                        ? new PlaceHoldResult(PlaceHoldStatus.InsufficientStock, null)
                        : new PlaceHoldResult(PlaceHoldStatus.ProductNotFound, null);
                }
            }

            await _holds.InsertOneAsync(session, DocumentMapper.ToDocument(hold), cancellationToken: cancellationToken);
            await session.CommitTransactionAsync(cancellationToken);
            return new PlaceHoldResult(PlaceHoldStatus.Placed, hold);
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            await AbortQuietlyAsync(session);
            if (hold.ClientRequestId is not null)
            {
                var existing = await _holds
                    .Find(Builders<HoldDocument>.Filter.Eq(document => document.ClientRequestId, hold.ClientRequestId))
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is not null)
                {
                    return new PlaceHoldResult(PlaceHoldStatus.AlreadyExists, DocumentMapper.ToHold(existing));
                }
            }

            throw;
        }
        catch
        {
            await AbortQuietlyAsync(session);
            throw;
        }
    }

    private async Task<TransitionResult> TransitionOnceAsync(
        string holdId,
        HoldStatus targetStatus,
        CancellationToken cancellationToken)
    {
        using var session = await _database.Client.StartSessionAsync(cancellationToken: cancellationToken);
        session.StartTransaction();
        try
        {
            var filter = Builders<HoldDocument>.Filter.Eq(hold => hold.Id, holdId)
                & Builders<HoldDocument>.Filter.Eq(hold => hold.Status, nameof(HoldStatus.Active));
            var update = Builders<HoldDocument>.Update.Set(hold => hold.Status, targetStatus.ToString());
            var options = new FindOneAndUpdateOptions<HoldDocument>
            {
                ReturnDocument = ReturnDocument.After
            };
            var updated = await _holds.FindOneAndUpdateAsync(session, filter, update, options, cancellationToken);
            if (updated is null)
            {
                await AbortQuietlyAsync(session);
                var current = await GetHoldAsync(holdId, cancellationToken);
                return current is null
                    ? new TransitionResult(TransitionStatus.NotFound, null)
                    : new TransitionResult(TransitionStatus.NotActive, current);
            }

            foreach (var item in updated.Items.OrderBy(line => line.ProductId, StringComparer.Ordinal))
            {
                var productFilter = Builders<ProductDocument>.Filter.Eq(product => product.Id, item.ProductId);
                var productUpdate = Builders<ProductDocument>.Update.Inc(product => product.AvailableQuantity, item.Quantity);
                await _products.UpdateOneAsync(session, productFilter, productUpdate, cancellationToken: cancellationToken);
            }

            await session.CommitTransactionAsync(cancellationToken);
            return new TransitionResult(TransitionStatus.Applied, DocumentMapper.ToHold(updated));
        }
        catch
        {
            await AbortQuietlyAsync(session);
            throw;
        }
    }

    private async Task<T> WithTransactionRetryAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (MongoException ex) when (
                attempt < maxAttempts
                && ex.HasErrorLabel("TransientTransactionError"))
            {
                _logger.LogWarning(ex, "Retrying aborted MongoDB transaction, attempt {Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private static async Task AbortQuietlyAsync(IClientSessionHandle session)
    {
        if (!session.IsInTransaction)
        {
            return;
        }

        try
        {
            await session.AbortTransactionAsync();
        }
        catch (Exception)
        {
            // The server may already have aborted the transaction.
        }
    }

    private static bool IsDuplicateKey(Exception exception) =>
        exception is MongoWriteException write && write.WriteError.Category == ServerErrorCategory.DuplicateKey
        || exception is MongoCommandException command && command.Code == 11000
        || exception is MongoBulkWriteException bulk && bulk.WriteErrors.Any(error => error.Category == ServerErrorCategory.DuplicateKey);
}
