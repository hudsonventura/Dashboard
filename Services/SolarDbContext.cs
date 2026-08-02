using System;
using System.IO;
using Dashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace Dashboard.Services;

public class SolarDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<SolarHistoryRecord> SolarHistoryRecords => Set<SolarHistoryRecord>();

    public SolarDbContext()
    {
        string basePath = AppContext.BaseDirectory;
        _dbPath = Path.Combine(basePath, "solar_history.db");
    }

    public SolarDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SolarHistoryRecord>(entity =>
        {
            entity.HasKey(e => e.Date);
            entity.Property(e => e.Date).HasMaxLength(10);
        });
    }
}
