using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Инициализируем и запускаем наш сервер
        var server = new DClinicHttpServer("http://localhost:8080/");
        await server.StartAsync();
    }
}

/// <summary>
/// Абстрактный базовый класс для HTTP-сервера.
/// Определяет общую логику жизненного цикла сервера.
/// </summary>
public abstract class HttpServerBase
{
    protected readonly string Prefix;
    protected HttpListener? Listener;

    protected HttpServerBase(string prefix)
    {
        Prefix = prefix;
    }

    public virtual async Task StartAsync()
    {
        Listener = new HttpListener();
        Listener.Prefixes.Add(Prefix);
        Listener.Start();

        OnServerStarted(Prefix);

        while (Listener.IsListening)
        {
            try
            {
                var context = await Listener.GetContextAsync();
                _ = Task.Run(() => HandleContextSafeAsync(context));
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }
    }

    private async Task HandleContextSafeAsync(HttpListenerContext context)
    {
        try
        {
            await HandleRequestAsync(context);
        }
        catch (Exception ex)
        {
            OnError(ex);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.OutputStream.Close();
        }
    }

    // Абстрактные методы, которые должны реализовать наследники
    protected abstract Task HandleRequestAsync(HttpListenerContext context);
    protected abstract void OnServerStarted(string prefix);
    protected abstract void OnError(Exception ex);
}

/// <summary>
/// Абстрактный класс для отдельных обработчиков запросов (маршрутов).
/// Аналог паттерна Handler / Controller.
/// </summary>
public abstract class BaseHttpHandler
{
    /// <summary>
    /// Проверяет, может ли данный обработчик обработать текущий путь.
    /// </summary>
    public abstract bool CanHandle(string path);

    /// <summary>
    /// Основная логика обработки запроса.
    /// </summary>
    public abstract Task HandleAsync(HttpListenerContext context);
}

/// <summary>
/// Конкретная реализация сервера для DClinic.
/// </summary>
public class DClinicHttpServer : HttpServerBase
{
    private readonly BaseHttpHandler[] _handlers;

    public DClinicHttpServer(string prefix) : base(prefix)
    {
        // Регистрируем наши обработчики (маршруты)
        _handlers = new BaseHttpHandler[]
        {
            new ApiClinicHandler(),
            new StaticFileHandler("wwwroot")
        };
    }

    protected override async Task HandleRequestAsync(HttpListenerContext context)
    {
        string path = context.Request.Url?.AbsolutePath ?? "/";
        Console.WriteLine($"[REQUEST] {context.Request.HttpMethod} {path}");

        // Ищем подходящий обработчик среди зарегистрированных
        foreach (var handler in _handlers)
        {
            if (handler.CanHandle(path))
            {
                await handler.HandleAsync(context);
                return;
            }
        }

        // Если никакой обработчик не подошел — 404
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        byte[] bytes = Encoding.UTF8.GetBytes("404 Not Found");
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.OutputStream.Close();
    }

    protected override void OnServerStarted(string prefix)
    {
        Console.WriteLine($"[INFO] DClinic сервер успешно запущен на: {prefix}");
        Console.WriteLine("[INFO] Нажми Ctrl+C для остановки.");
    }

    protected override void OnError(Exception ex)
    {
        Console.WriteLine($"[ERROR] {ex.Message}");
    }
}

/// <summary>
/// Обработчик API запросов клиники (наследник BaseHttpHandler)
/// </summary>
public class ApiClinicHandler : BaseHttpHandler
{
    public override bool CanHandle(string path)
    {
        return path.StartsWith("/api/clinic");
    }

    public override async Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;
        string jsonResponse = "{\"status\": \"success\", \"message\": \"Welcome to DClinic API with Abstract Classes!\"}";
        byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);

        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        response.StatusCode = (int)HttpStatusCode.OK;

        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }
}

/// <summary>
/// Обработчик статических файлов (наследник BaseHttpHandler)
/// </summary>
public class StaticFileHandler : BaseHttpHandler
{
    private readonly string _staticDir;

    public StaticFileHandler(string staticDir)
    {
        _staticDir = staticDir;
    }

    public override bool CanHandle(string path)
    {
        // Статический обработчик берет на себя все, что не относится к API
        return !path.StartsWith("/api/");
    }

    public override async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        string path = request.Url?.AbsolutePath ?? "/";
        if (path == "/")
        {
            path = "/index.html";
        }

        string filePath = Path.Combine(Directory.GetCurrentDirectory(), _staticDir, path.TrimStart('/'));

        if (File.Exists(filePath))
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            response.ContentType = GetContentType(filePath);
            response.ContentLength64 = fileBytes.Length;
            response.StatusCode = (int)HttpStatusCode.OK;

            await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
        }
        else
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            byte[] notFoundBytes = Encoding.UTF8.GetBytes("<h1>404 Not Found</h1>");
            response.ContentLength64 = notFoundBytes.Length;
            await response.OutputStream.WriteAsync(notFoundBytes, 0, notFoundBytes.Length);
        }

        response.OutputStream.Close();
    }

    private string GetContentType(string path)
    {
        string extension = Path.GetExtension(path).ToLower();
        return extension switch
        {
            ".html" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}