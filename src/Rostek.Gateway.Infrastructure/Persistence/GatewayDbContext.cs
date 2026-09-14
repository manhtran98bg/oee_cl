using Microsoft.EntityFrameworkCore;
using Rostek.Gateway.Domain.Entities;

namespace Rostek.Gateway.Infrastructure.Persistence;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    public DbSet<MachineGroup> MachineGroups => Set<MachineGroup>();
    public DbSet<MachineTemplate> MachineTemplates => Set<MachineTemplate>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<MachineConnection> MachineConnections => Set<MachineConnection>();
    public DbSet<TemplateSignal> TemplateSignals => Set<TemplateSignal>();
    public DbSet<MachineSignalOverride> MachineSignalOverrides => Set<MachineSignalOverride>();
    public DbSet<ConfigurationVersion> ConfigurationVersions => Set<ConfigurationVersion>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MachineGroup>(entity =>
        {
            entity.ToTable("MachineGroups");
            entity.HasKey(group => group.Id);
            entity.Property(group => group.Code).HasMaxLength(100).IsRequired();
            entity.Property(group => group.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(group => group.Code).IsUnique();
        });

        modelBuilder.Entity<MachineTemplate>(entity =>
        {
            entity.ToTable("MachineTemplates");
            entity.HasKey(template => template.Id);
            entity.Property(template => template.Code).HasMaxLength(100).IsRequired();
            entity.Property(template => template.Name).HasMaxLength(200).IsRequired();
            entity.Property(template => template.Protocol).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.HasIndex(template => template.Code).IsUnique();
            entity.HasMany(template => template.Signals).WithOne(signal => signal.Template).HasForeignKey(signal => signal.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Machine>(entity =>
        {
            entity.ToTable("Machines");
            entity.HasKey(machine => machine.Id);
            entity.Property(machine => machine.Code).HasMaxLength(100).IsRequired();
            entity.Property(machine => machine.Name).HasMaxLength(200).IsRequired();
            entity.Property(machine => machine.Model).HasMaxLength(200);
            entity.Property(machine => machine.Serial).HasMaxLength(200);
            entity.Property(machine => machine.Manufacturer).HasMaxLength(200);
            entity.Property(machine => machine.Location).HasMaxLength(200);
            entity.HasIndex(machine => machine.Code).IsUnique();
            entity.HasIndex(machine => machine.GroupId);
            entity.HasIndex(machine => machine.TemplateId);
            entity.HasOne(machine => machine.Group).WithMany().HasForeignKey(machine => machine.GroupId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(machine => machine.Template).WithMany().HasForeignKey(machine => machine.TemplateId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(machine => machine.Connection).WithOne(connection => connection.Machine).HasForeignKey<MachineConnection>(connection => connection.MachineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(machine => machine.SignalOverrides).WithOne(signalOverride => signalOverride.Machine).HasForeignKey(signalOverride => signalOverride.MachineId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MachineConnection>(entity =>
        {
            entity.ToTable("MachineConnections");
            entity.HasKey(connection => connection.Id);
            entity.Property(connection => connection.Protocol).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.HasIndex(connection => connection.MachineId).IsUnique();
        });

        modelBuilder.Entity<TemplateSignal>(entity =>
        {
            entity.ToTable("TemplateSignals");
            entity.HasKey(signal => signal.Id);
            entity.Property(signal => signal.SignalCode).HasMaxLength(100).IsRequired();
            entity.Property(signal => signal.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(signal => signal.SourceAddress).HasMaxLength(500).IsRequired();
            entity.Property(signal => signal.DataType).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(signal => signal.AccessMode).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.HasIndex(signal => signal.TemplateId);
            entity.HasIndex(signal => new { signal.TemplateId, signal.SignalCode }).IsUnique();
        });

        modelBuilder.Entity<MachineSignalOverride>(entity =>
        {
            entity.ToTable("MachineSignalOverrides");
            entity.HasKey(signalOverride => signalOverride.Id);
            entity.Property(signalOverride => signalOverride.DataType).HasConversion<string>();
            entity.HasIndex(signalOverride => signalOverride.MachineId);
            entity.HasIndex(signalOverride => new { signalOverride.MachineId, signalOverride.TemplateSignalId }).IsUnique();
            entity.HasOne(signalOverride => signalOverride.TemplateSignal).WithMany().HasForeignKey(signalOverride => signalOverride.TemplateSignalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConfigurationVersion>(entity =>
        {
            entity.ToTable("ConfigurationVersions");
            entity.HasKey(version => version.Id);
            entity.Property(version => version.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(version => version.SnapshotJson).IsRequired();
            entity.Property(version => version.Checksum).HasMaxLength(128).IsRequired();
            entity.HasIndex(version => version.Version).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Action).HasMaxLength(200).IsRequired();
            entity.Property(log => log.EntityType).HasMaxLength(100).IsRequired();
            entity.HasIndex(log => log.CreatedAtUtc);
        });
    }
}
