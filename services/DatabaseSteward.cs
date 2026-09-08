using Microsoft.Extensions.Configuration;
using System;

namespace DYPStore.Services
{
    public class DatabaseSteward
    {
        private readonly IConfiguration _configuration;
        public bool IsUsingSecondaryDb { get; private set; } = false;

        public DatabaseSteward(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetConnectionString()
        {
            try
            {
                // 1. Intentar leer variables de entorno
                string envConn = Environment.GetEnvironmentVariable("PRIMARY_DB_CONNECTION") 
                              ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION");

                if (!string.IsNullOrEmpty(envConn))
                {
                    return envConn;
                }
            }
            catch
            {
                // Ignorar excepciones al leer variables de entorno
            }

            // 2. Intentar leer appsettings
            string primaryConfig = _configuration.GetConnectionString("PrimarySupabase") 
                                ?? _configuration.GetConnectionString("DefaultConnection");

            if (!string.IsNullOrEmpty(primaryConfig))
            {
                return primaryConfig;
            }

            // 3. Fallback directo a Aiven PostgreSQL
            return "Host=pg-cc4b12f-dypstore2026-77e3.a.aivencloud.com;Port=28541;Database=defaultdb;Username=avnadmin;Password=AVNS_fCtSlob8Z5sI0el0S6t;SSL Mode=Require;Trust Server Certificate=true;";
        }
    }
}