using DYPStore.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace DYPStore
{
    public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(DesignTimeConnectionStringProvider.GetConnectionString());
            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }

    public class DataProtectionKeyContextFactory : IDesignTimeDbContextFactory<DataProtectionKeyContext>
    {
        public DataProtectionKeyContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<DataProtectionKeyContext>();
            optionsBuilder.UseNpgsql(DesignTimeConnectionStringProvider.GetConnectionString());
            return new DataProtectionKeyContext(optionsBuilder.Options);
        }
    }

    internal static class DesignTimeConnectionStringProvider
    {
        public static string GetConnectionString()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = config.GetConnectionString("PrimarySupabase")
                                   ?? config.GetConnectionString("SecondaryNeon");
            if (!string.IsNullOrWhiteSpace(connectionString))
                return connectionString;

            // Fallback local string used solo en tiempo de diseño para generar migraciones.
            // En tiempo de ejecución se usan las variables de entorno de Render.
            return "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        }
    }
}
