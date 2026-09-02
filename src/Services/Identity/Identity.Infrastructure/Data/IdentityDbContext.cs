using Identity.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Data
{
    public class IdentityDbContext : DbContext
    {
        public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options) { }
        public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ApplicationUser>().HasKey(u => u.Id);
            modelBuilder.Entity<ApplicationUser>().HasIndex(u => u.Email).IsUnique();
        }
    }
}
