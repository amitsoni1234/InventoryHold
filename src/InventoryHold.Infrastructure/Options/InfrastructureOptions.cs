namespace InventoryHold.Infrastructure.Options;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "";

    public string DatabaseName { get; set; } = "inventory_hold";
}

public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    public string ConnectionString { get; set; } = "";

    public int InventoryTtlSeconds { get; set; } = 10;
}

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string Username { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    public string Exchange { get; set; } = "inventory.holds";
}
