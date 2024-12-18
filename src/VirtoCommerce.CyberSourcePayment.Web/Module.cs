using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        //serviceCollection.AddTransient<ICyberSourceCheckoutService, CyberSourceCheckoutService>();
        serviceCollection.AddTransient<ICyberSourceClient, CyberSourceClient>();
        serviceCollection.AddTransient<CyberSourcePaymentMethod>();
    }

    public void PostInitialize(IApplicationBuilder appBuilder)
    {
        var settingsRegistrar = appBuilder.ApplicationServices.GetRequiredService<ISettingsRegistrar>();
        settingsRegistrar.RegisterSettings(ModuleConstants.Settings.AllSettings, ModuleInfo.Id);
        var paymentMethodsRegistrar = appBuilder.ApplicationServices.GetRequiredService<IPaymentMethodsRegistrar>();
        paymentMethodsRegistrar.RegisterPaymentMethod(() =>
            appBuilder.ApplicationServices.GetService<CyberSourcePaymentMethod>());

        settingsRegistrar.RegisterSettingsForType(ModuleConstants.Settings.General.AllGeneralSettings, nameof(CyberSourcePaymentMethod));

    }

    public void Uninstall()
    {
        // Nothing to do here
    }
}
