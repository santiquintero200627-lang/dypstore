using Npgsql;
using System.Net.Sockets;

namespace DYPStore.Services
{
    public class DatabaseSteward
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseSteward> _logger;
        private string? _activeConnectionString;
        private bool _isUsingSecondaryDb;

        public bool IsUsingSecondaryDb => _isUsingSecondaryDb;

        public DatabaseSteward(IConfiguration configuration, ILogger<DatabaseSteward> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public string GetConnectionString()
        {
            if (_activeConnectionString != null)
                return _activeConnectionString;

            var primary = _configuration.GetConnectionString("PrimarySupabase");
            var secondary = _configuration.GetConnectionString("SecondaryNeon");

            if (string.IsNullOrWhiteSpace(primary))
            {
                if (string.IsNullOrWhiteSpace(secondary))
                    throw new InvalidOperationException("Las cadenas de conexión no están configuradas.");

                ValidateConnectionString(secondary, "SecondaryNeon");
                _isUsingSecondaryDb = true;
                _activeConnectionString = secondary!;
                return _activeConnectionString;
            }

            try
            {
                using var primaryConnection = new NpgsqlConnection(primary);
                primaryConnection.Open();
                _isUsingSecondaryDb = false;
                _logger.LogInformation("Conectado exitosamente a PrimarySupabase.");
                _activeConnectionString = primary;
                return _activeConnectionString;
            }
            catch (Exception exPrimary)
            {
                _logger.LogWarning(exPrimary, "Fallo al conectar a PrimarySupabase.");
                if (string.IsNullOrWhiteSpace(secondary))
                    throw new InvalidOperationException("No se pudo conectar a PrimarySupabase y no hay SecondaryNeon configurada.", exPrimary);

                ValidateConnectionString(secondary, "SecondaryNeon");

                try
                {
                    using var secondaryConnection = new NpgsqlConnection(secondary);
                    secondaryConnection.Open();
                    _isUsingSecondaryDb = true;
                    _logger.LogInformation("Conectado exitosamente a SecondaryNeon.");
                    _activeConnectionString = secondary;
                    return _activeConnectionString;
                }
                catch (Exception exSecondary)
                {
                    _logger.LogWarning(exSecondary, "Fallo al conectar a SecondaryNeon.");
                    throw new InvalidOperationException("No se pudo conectar a ninguna base de datos válida.", exSecondary);
                }
            }
        }

        private static void ValidateConnectionString(string connectionString, string name)
        {
            try
            {
                _ = new NpgsqlConnectionStringBuilder(connectionString);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"La cadena de conexión {name} no tiene un formato válido.", ex);
            }
        }
    }
}
