using System.Data;
using Npgsql;

namespace app.Data;

// Clase para gestionar la conexión a la base de datos PostgreSQL
public class Database
{
    // Cadena de conexión obtenida de la configuración
    private readonly string _connectionString;

    // Constructor que recibe la configuración mediante inyección de dependencias
    public Database(IConfiguration configuration)
    {
        // Obtener la cadena de conexión desde la configuración de la aplicación
        // Si no se encuentra, lanza una excepción indicando que es requerida
        _connectionString = configuration.GetConnectionString("PostgreSQL")
                            ?? throw new ArgumentNullException(nameof(_connectionString));
    }

    // Método para crear una nueva conexión a la base de datos
    // Devuelve una interfaz IDbConnection para mayor flexibilidad
    public IDbConnection CreateConnection()
    {
        // Crear y devolver una nueva conexión de NpgsqlConnection utilizando la cadena de conexión
        return new NpgsqlConnection(_connectionString);
    }
}