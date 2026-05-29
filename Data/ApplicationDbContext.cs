using DYPStore.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DYPStore.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Product> Products { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<ChatLog> ChatLogs { get; set; }
        public DbSet<FaceEnrollment> FaceEnrollments { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<CartItem>()
                .HasOne(c => c.User)
                .WithMany(u => u.CartItems)
                .HasForeignKey(c => c.UserId);

            builder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Order>()
                .Property(o => o.Status)
                .HasConversion<string>();

            builder.Entity<Product>()
                .Property(p => p.Category)
                .HasConversion<string>();

            builder.Entity<ChatLog>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);

            builder.Entity<FaceEnrollment>()
                .HasOne(f => f.User)
                .WithMany()
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(true);

            // One enrollment per user (unique index)
            builder.Entity<FaceEnrollment>()
                .HasIndex(f => f.UserId)
                .IsUnique();

            // --- RESTRICCIONES CHECK DE BASE DE DATOS ---
            builder.Entity<Product>()
                .ToTable(t => {
                    t.HasCheckConstraint("CK_Product_Stock_Positive", "\"Stock\" >= 0");
                    t.HasCheckConstraint("CK_Product_Price_Positive", "\"Price\" >= 0");
                });

            builder.Entity<Order>()
                .ToTable(t => t.HasCheckConstraint("CK_Order_Total_Positive", "\"Total\" >= 0"));

            builder.Entity<OrderItem>()
                .ToTable(t => {
                    t.HasCheckConstraint("CK_OrderItem_Quantity_Positive", "\"Quantity\" > 0");
                    t.HasCheckConstraint("CK_OrderItem_UnitPrice_Positive", "\"UnitPrice\" >= 0");
                });

            builder.Entity<CartItem>()
                .ToTable(t => t.HasCheckConstraint("CK_CartItem_Quantity_Positive", "\"Quantity\" > 0"));
        }
    }
}
