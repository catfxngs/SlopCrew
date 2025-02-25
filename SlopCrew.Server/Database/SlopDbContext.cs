﻿using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace SlopCrew.Server.Database;

public class SlopDbContext : DbContext {
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Crew> Crews { get; set; } = null!;

    public SlopDbContext() { }
    public SlopDbContext(DbContextOptions<SlopDbContext> options) : base(options) { }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) {
        if (!optionsBuilder.IsConfigured) optionsBuilder.UseSqlite();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        // Crew.Members <- many to many -> User.Crews
        modelBuilder.Entity<Crew>()
            .HasMany(c => c.Members)
            .WithMany(u => u.Crews);

        // Crew.Owners <- many to many -> User.OwnedCrews
        modelBuilder.Entity<Crew>()
            .HasMany(c => c.Owners)
            .WithMany(u => u.OwnedCrews);

        // User.RepresentingCrew -> one to one -> Crew
        modelBuilder.Entity<User>()
            .HasOne(u => u.RepresentingCrew)
            .WithOne()
            .HasForeignKey<User>(u => u.RepresentingCrewId);

        // Invite codes are just lists of strings
        modelBuilder.Entity<Crew>()
            .Property(c => c.InviteCodes)
            .HasConversion(
                v => string.Join(' ', v),
                v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            );

        // Crew.SuperOwner -> one to many -> User.SuperOwnedCrews
        modelBuilder.Entity<Crew>()
            .HasOne(c => c.SuperOwner)
            .WithMany(u => u.SuperOwnedCrews);
    }
}
