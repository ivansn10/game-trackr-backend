using app.Model;

namespace app.Services;

/// <summary>
///     Clases para deserializar respuestas mejoradas de Weaviate
/// </summary>
public class WeaviateEnhancedResponse
{
    public EnhancedDataContainer? Data { get; set; }
}

public class EnhancedDataContainer
{
    public EnhancedGetContainer? Get { get; set; }
}

public class EnhancedGetContainer
{
    public List<EnhancedGameRecommendation>? GameRecommendation { get; set; }
}

/// <summary>
///     Clases para deserializar respuestas básicas de Weaviate
/// </summary>
public class WeaviateResponse
{
    public DataContainer? Data { get; set; } // Contenedor principal de datos
}

public class DataContainer
{
    public GetContainer? Get { get; set; } // Contenedor de resultados de la consulta
}

public class GetContainer
{
    public List<GameRecommendation>? GameRecommendation { get; set; } // Lista de recomendaciones
}