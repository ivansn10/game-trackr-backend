using System.Text.Json.Serialization;
using app.Data;
using app.Model;
using app.repositories;
using app.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

var builder = WebApplication.CreateBuilder(args);

// Configuración
builder.Services.Configure<ServiceUrls>(builder.Configuration.GetSection("Services"));
builder.Services.Configure<IGDBConfig>(builder.Configuration.GetSection("Services:IGDB"));
builder.Services.Configure<WeaviateConfig>(builder.Configuration.GetSection("Services:Weaviate"));

// Controladores y JSON
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();   

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("Database")
    .AddCheck<WeaviateHealthCheck>("Weaviate");

// Memoria cache simple
builder.Services.AddMemoryCache();

// Repositorios y servicios
builder.Services.AddSingleton<GameRepository>();
builder.Services.AddScoped<GameStatusRepository>();
builder.Services.AddScoped<RatingRepository>();
builder.Services.AddScoped<RecommendationRepository>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<IGDBAuthService>();
builder.Services.AddScoped<IGDBService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WeaviateService>();
builder.Services.AddSingleton<Database>();

// HTTP Clients
builder.Services.AddHttpClient<IGDBAuthService>(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient<IGDBService>(client => client.Timeout = TimeSpan.FromSeconds(30));

builder.Services.AddHttpClient<WeaviateService>(client => client.Timeout = TimeSpan.FromSeconds(60));

builder.Services.AddDistributedMemoryCache();

// Sesiones
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });

    options.AddPolicy("AllowProduction", policy =>
    {
        policy.WithOrigins(
                "https://iserrano.dev",
                "https://www.iserrano.dev",
                "https://api-gametrackr.iserrano.dev"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "GameCatalogAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.None
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;

        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = 401;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = 403;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/root/.aspnet/DataProtection-Keys"))
    .SetApplicationName("GameTracker");

builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });

// Build app
var app = builder.Build();

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseResponseCompression();

app.MapHealthChecks("/health");

app.UseCors(app.Environment.IsDevelopment() ? "AllowAngularDev" : "AllowProduction");

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();


// Clases adicionales necesarias

/// <summary>
///     Caché distribuida con fallback a memoria local en caso de error
/// </summary>
public class FallbackDistributedCache(
    IDistributedCache primaryCache,
    IMemoryCache fallbackCache,
    ILogger<FallbackDistributedCache>? logger = null) : IDistributedCache
{
    private readonly IMemoryCache _fallbackCache = fallbackCache;

    private readonly ILogger<FallbackDistributedCache>
        _logger = logger ?? NullLogger<FallbackDistributedCache>.Instance;

    private readonly IDistributedCache _primaryCache = primaryCache;

    public byte[] Get(string key)
    {
        try
        {
            return _primaryCache.Get(key) ?? Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al obtener datos de caché primaria, usando fallback");
            return _fallbackCache.Get<byte[]>(key) ?? Array.Empty<byte>();
        }
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        try
        {
            return _primaryCache.GetAsync(key, token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al obtener datos de caché primaria, usando fallback");
            return Task.FromResult(_fallbackCache.Get<byte[]>(key));
        }
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        try
        {
            _primaryCache.Set(key, value, options);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al guardar en caché primaria, usando fallback");

            var memoryCacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = options.SlidingExpiration
            };

            _fallbackCache.Set(key, value, memoryCacheOptions);
        }
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        try
        {
            return _primaryCache.SetAsync(key, value, options, token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al guardar en caché primaria, usando fallback");

            var memoryCacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
                SlidingExpiration = options.SlidingExpiration
            };

            _fallbackCache.Set(key, value, memoryCacheOptions);
            return Task.CompletedTask;
        }
    }

    public void Refresh(string key)
    {
        try
        {
            _primaryCache.Refresh(key);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al refrescar en caché primaria");
        }
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        try
        {
            return _primaryCache.RefreshAsync(key, token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al refrescar en caché primaria");
            return Task.CompletedTask;
        }
    }

    public void Remove(string key)
    {
        try
        {
            _primaryCache.Remove(key);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al eliminar de caché primaria");
        }

        // Siempre eliminar de la caché de respaldo
        _fallbackCache.Remove(key);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        try
        {
            return _primaryCache.RemoveAsync(key, token);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error al eliminar de caché primaria");
        }

        // Siempre eliminar de la caché de respaldo
        _fallbackCache.Remove(key);
        return Task.CompletedTask;
    }
}

/// <summary>
///     Health check para validar conexión a la base de datos
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly Database _database;

    public DatabaseHealthCheck(Database database)
    {
        _database = database;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _database.CreateConnection();
            connection.Open(); // Método sincrónico

            // Ejecutar una consulta simple
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            var result = command.ExecuteScalar(); // Método sincrónico

            if (result != null) return Task.FromResult(HealthCheckResult.Healthy("Conexión a base de datos OK"));

            return Task.FromResult(HealthCheckResult.Degraded("La consulta no devolvió resultados"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Error en conexión a base de datos", ex));
        }
    }
}

/// <summary>
///     Health check para validar conexión a Weaviate
/// </summary>
public class WeaviateHealthCheck : IHealthCheck
{
    private readonly WeaviateService _weaviateService;

    public WeaviateHealthCheck(WeaviateService weaviateService)
    {
        _weaviateService = weaviateService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _weaviateService.CheckHealth();

            if (status is { } objStatus &&
                objStatus.GetType().GetProperty("Status")?.GetValue(objStatus)?.ToString() == "Healthy")
                return HealthCheckResult.Healthy("Conexión a Weaviate OK");

            return HealthCheckResult.Degraded("Weaviate retornó estado degradado");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Error en conexión a Weaviate", ex);
        }
    }
}