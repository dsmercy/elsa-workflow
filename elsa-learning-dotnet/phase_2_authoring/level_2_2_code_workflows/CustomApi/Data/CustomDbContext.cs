using Microsoft.EntityFrameworkCore;

namespace CustomApi.Data;

/// <summary>
/// custom.sqlite.db — orders, approvals and notifications. Elsa's own tables are in the Elsa Server's
/// elsa.sqlite.db; the two databases never share a connection.
/// </summary>
public class CustomDbContext(DbContextOptions<CustomDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.Property(o => o.Pattern).HasConversion<string>();
            e.Property(o => o.CreatedBy).HasConversion<string>().HasDefaultValue(OrderCreator.Customer);
            // SQLite has no decimal type; store it as TEXT so values round-trip exactly.
            e.Property(o => o.Amount).HasConversion<string>();
            e.HasIndex(o => o.CreatedAt);
        });
        modelBuilder.Entity<Approval>(e =>
        {
            e.Property(a => a.Pattern).HasConversion<string>();
            e.HasIndex(a => new { a.Status, a.OrderId });
            e.HasIndex(a => a.TaskId);
        });
        modelBuilder.Entity<Notification>(e => e.HasIndex(n => n.ReceivedAt));
    }

    /// <summary>
    /// LEARNING-ONLY schema upgrade for databases created before Orders.CreatedBy existed
    /// (EnsureCreated never alters an existing database). A real app would add an EF Core migration.
    /// </summary>
    public void UpgradeSchema()
    {
        var connection = Database.GetDbConnection();
        connection.Open();
        try
        {
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Orders') WHERE name = 'CreatedBy'";
            if (Convert.ToInt64(check.ExecuteScalar()) == 0)
                Database.ExecuteSqlRaw("ALTER TABLE \"Orders\" ADD COLUMN \"CreatedBy\" TEXT NOT NULL DEFAULT 'Customer'");
        }
        finally
        {
            connection.Close();
        }
    }
}
