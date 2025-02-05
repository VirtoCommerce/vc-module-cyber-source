using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using VirtoCommerce.CyberSourcePayment.Core;
using VirtoCommerce.CyberSourcePayment.Core.Models;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.CyberSourcePayment.Data.Providers;
using VirtoCommerce.CyberSourcePayment.Data.Services;
using VirtoCommerce.PaymentModule.Core.Services;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.Platform.Core.Settings;

namespace VirtoCommerce.CyberSourcePayment.Web;

public class Module : IModule, IHasConfiguration
{
    public ManifestModuleInfo ModuleInfo { get; set; }
    public IConfiguration Configuration { get; set; }

    public void Initialize(IServiceCollection serviceCollection)
    {
        serviceCollection.AddOptions<CyberSourcePaymentMethodOptions>().Bind(Configuration.GetSection("Payments:CyberSource")).ValidateDataAnnotations();

        serviceCollection.AddTransient<ICyberSourceClient, CyberSourceClient>();
        serviceCollection.AddTransient<CyberSourcePaymentMethod>();
        serviceCollection.AddTransient<CyberSourceJwkValidator>();
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        var settingsRegistrar = appBuilder.ApplicationServices.GetRequiredService<ISettingsRegistrar>();
        settingsRegistrar.RegisterSettings(ModuleConstants.Settings.AllSettings, ModuleInfo.Id);
        var paymentMethodsRegistrar = appBuilder.ApplicationServices.GetRequiredService<IPaymentMethodsRegistrar>();
        paymentMethodsRegistrar.RegisterPaymentMethod(() =>
            appBuilder.ApplicationServices.GetService<CyberSourcePaymentMethod>());

        settingsRegistrar.RegisterSettingsForType(ModuleConstants.Settings.General.AllGeneralSettings, nameof(CyberSourcePaymentMethod));

        JsonConvert.DefaultSettings = () => new JsonSerializerSettings
        {
            Converters = { new Notificationsubscriptionsv1webhooksNotificationScopeJsonConverter() }
        };

        appBuilder.UseMiddleware<RequestLoggingMiddleware>(); // todo: remove it
    }

    public void Uninstall()
    {
        // Nothing to do here
    }
}

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public RequestLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            var request = context.Request;

            // Записываем метод запроса и полный URL
            var logContent = new StringBuilder();
            logContent.AppendLine($"Timestamp: {DateTime.UtcNow}");
            logContent.AppendLine($"Method: {request.Method}");
            logContent.AppendLine($"URL: {request.Path}{request.QueryString}");

            // Логируем заголовки
            logContent.AppendLine("Headers:");
            foreach (var header in request.Headers)
            {
                logContent.AppendLine($"{header.Key}: {header.Value}");
            }

            // Читаем тело запроса
            if (request.ContentLength > 0 || request.Method == "POST" || request.Method == "PUT")
            {
                request.EnableBuffering(); // Разрешаем повторное чтение тела
                using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                request.Body.Position = 0; // Сбрасываем позицию потока

                logContent.AppendLine("Body:");
                logContent.AppendLine(body);
            }

            logContent.AppendLine(new string('-', 50)); // Разделитель

            // Записываем в файл
            var logFilePath = Path.Combine("requests-cybersource.log");
            await File.AppendAllTextAsync(logFilePath, logContent.ToString());

            Console.WriteLine(logContent.ToString());


            // Передаём запрос дальше по pipeline
            await _next(context);
        }
        catch (Exception ex)
        {
            await File.AppendAllTextAsync("Logs/errors.log", $"[{DateTime.UtcNow}] ERROR: {ex.Message}\n");
            throw;
        }
    }
}
