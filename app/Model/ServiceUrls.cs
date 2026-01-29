namespace app.Model;

/// <summary>
///     Clase que almacena las URLs y configuraciones de los diferentes servicios
///     utilizados por la aplicación.
/// </summary>
public class ServiceUrls
{
   /// <summary>
   ///     URL base de la API de la aplicación.
   /// </summary>
   public string API { get; set; } = string.Empty;

   /// <summary>
   ///     URL del servidor Nginx que sirve como proxy inverso.
   /// </summary>
   public string Nginx { get; set; } = string.Empty;

   /// <summary>
   ///     Configuración para conectarse a la API de IGDB.
   /// </summary>
   public IGDBConfig IGDB { get; set; } = new();

   /// <summary>
   ///     Configuración para conectarse a Weaviate (motor de IA vectorial).
   /// </summary>
   public WeaviateConfig Weaviate { get; set; } = new();
}

/// <summary>
///     Configuración para acceder a la API de IGDB.
/// </summary>
public class IGDBConfig
{
   /// <summary>
   ///     ID de cliente proporcionado por IGDB/Twitch para autenticación.
   /// </summary>
   public string ClientId { get; set; } = string.Empty;

   /// <summary>
   ///     Secreto de cliente proporcionado por IGDB/Twitch para autenticación.
   /// </summary>
   public string ClientSecret { get; set; } = string.Empty;

   /// <summary>
   ///     Token de acceso obtenido tras autenticación (almacenado temporalmente).
   /// </summary>
   public string AccessToken { get; set; } = string.Empty;
}

/// <summary>
///     Configuración para acceder a Weaviate.
/// </summary>
public class WeaviateConfig
{
   /// <summary>
   ///     URL de la API de Weaviate.
   /// </summary>
   public string ApiUrl { get; set; } = string.Empty;
}