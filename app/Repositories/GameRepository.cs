using app.Data;
using app.Model;
using Dapper;

namespace app.repositories;

/// <summary>
///     Repositorio que maneja operaciones de acceso a datos para la entidad Game.
/// </summary>
public class GameRepository
{
    // Conexión a la base de datos
    private readonly Database _database;

    // Logger para registro de eventos
    private readonly ILogger<GameRepository> _logger;

    /// <summary>
    ///     Constructor que recibe la dependencia de acceso a datos y logger.
    /// </summary>
    public GameRepository(Database database, ILogger<GameRepository> logger)
    {
        _database = database;
        _logger = logger;
    }


    /// <summary>
    ///     Obtiene todos los juegos registrados en la base de datos.
    /// </summary>
    /// <returns>Colección de todos los juegos.</returns>
    public async Task<IEnumerable<Game>> GetAll()
    {
        try
        {
            // Creamos una conexión nueva a la base de datos
            using var connection = _database.CreateConnection();
            // Ejecutamos una consulta SQL simple para obtener todos los juegos
            return await connection.QueryAsync<Game>("SELECT * FROM Games;");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener todos los juegos");
            throw;
        }
    }

    /// <summary>
    ///     Obtiene múltiples juegos por sus IDs.
    /// </summary>
    /// <param name="ids">Lista de IDs de juegos a buscar.</param>
    /// <returns>Lista de juegos encontrados.</returns>
    public async Task<List<Game>> GetGamesByIds(List<int> ids)
    {
        try
        {
            using var connection = _database.CreateConnection();
            // Consulta que usa el operador ANY para buscar múltiples IDs en PostgreSQL
            var query = "SELECT * FROM Games WHERE GameId = ANY(@Ids);";
            return (await connection.QueryAsync<Game>(query, new { Ids = ids })).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener juegos por IDs: {IDs}", string.Join(", ", ids));
            throw;
        }
    }

    /// <summary>
    ///     Obtiene un juego específico por su ID.
    /// </summary>
    /// <param name="id">ID del juego a buscar.</param>
    /// <returns>El juego encontrado o null si no existe.</returns>
    public async Task<Game?> GetById(int id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            // Usamos QueryFirstOrDefaultAsync para obtener un solo registro o null
            return await connection.QueryFirstOrDefaultAsync<Game>(
                "SELECT * FROM Games WHERE GameId = @Id;", new { Id = id }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener juego con ID {GameId}", id);
            throw;
        }
    }

    /// <summary>
    ///     Agrega un nuevo juego a la base de datos.
    /// </summary>
    /// <param name="game">Objeto juego con los datos a insertar.</param>
    public async Task Create(Game game)
    {
        // Verificación adicional de seguridad
        if (game.GameTitle == null)
        {
            game.GameTitle = $"Juego IGDB {game.IgdbId}";
            _logger.LogWarning("Corrigiendo GameTitle nulo para IgdbId {IgdbId}", game.IgdbId);
        }

        using var connection = _database.CreateConnection();

        // Ejecutar consulta SQL con doble verificación de los parámetros
        var sql = @"
        INSERT INTO Games (IgdbId, GameTitle) 
        VALUES (@IgdbId, @GameTitle)
        RETURNING GameId;";

        // Crear diccionario de parámetros explícito para mayor control
        var parameters = new Dictionary<string, object>
        {
            { "IgdbId", game.IgdbId },
            { "GameTitle", game.GameTitle ?? $"Juego IGDB {game.IgdbId}" } // Asegurar que nunca sea nulo
        };

        try
        {
            // Ejecutar la consulta con parámetros explícitos
            var gameId = await connection.ExecuteScalarAsync<int>(sql, parameters);
            // Asignar el ID generado al objeto
            game.GameId = gameId;
            _logger.LogDebug("Juego creado correctamente: ID={GameId}, Título={Title}", gameId, game.GameTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando juego en base de datos: IgdbId={IgdbId}, GameTitle={GameTitle}",
                game.IgdbId, game.GameTitle);
            throw; // Re-lanzar excepción para manejo superior
        }
    }

    /// <summary>
    ///     Actualiza la información de un juego existente.
    /// </summary>
    /// <param name="game">Objeto juego con los datos actualizados.</param>
    public async Task Update(Game game)
    {
        try
        {
            using var connection = _database.CreateConnection();
            // Actualizar solo el IgdbId del juego especificado
            await connection.ExecuteAsync(
                "UPDATE Games SET IgdbId = @IgdbId, GameTitle = @GameTitle WHERE GameId = @GameId;",
                game
            );
            _logger.LogDebug("Juego actualizado correctamente: ID={GameId}", game.GameId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar juego con ID {GameId}", game.GameId);
            throw;
        }
    }

    /// <summary>
    ///     Elimina un juego de la base de datos.
    /// </summary>
    /// <param name="id">ID del juego a eliminar.</param>
    public async Task Delete(int id)
    {
        try
        {
            using var connection = _database.CreateConnection();
            // Eliminar el juego con el ID especificado
            await connection.ExecuteAsync("DELETE FROM Games WHERE GameId = @Id;", new { Id = id });
            _logger.LogDebug("Juego eliminado correctamente: ID={GameId}", id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar juego con ID {GameId}", id);
            throw;
        }
    }
}