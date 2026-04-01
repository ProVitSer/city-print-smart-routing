using CityPrintSmartRouting.Models;
using Microsoft.EntityFrameworkCore;

namespace CityPrintSmartRouting.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<SyncLog> SyncLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ClientPhone — основной ключ дедубликации
        modelBuilder.Entity<Contact>()
            .HasIndex(c => c.ClientPhone)
            .IsUnique();
    }
}
