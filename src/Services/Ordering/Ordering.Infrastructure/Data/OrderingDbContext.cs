using Microsoft.EntityFrameworkCore;
using Ordering.Infrastructure.Models;

namespace Ordering.Infrastructure.Data
{
    public class OrderingDbContext : DbContext
    {
        public OrderingDbContext(DbContextOptions<OrderingDbContext> options) : base(options) { }
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderItem> OrderItems=> Set<OrderItem>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
        }
    }
}
