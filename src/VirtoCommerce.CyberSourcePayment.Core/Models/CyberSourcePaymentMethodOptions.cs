using System.Collections.Generic;

namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourcePaymentMethodOptions
{
    public string MerchantId { get; set; }
    public string MerchantKeyId { get; set; }
    public string MerchantSecretKey { get; set; }

    public bool ValidateSignature { get; set; } = true;

    private readonly Dictionary<string, string> _configurationDictionary = new();

    public string Environment(bool sandbox)
    {
        return sandbox
            ? "apitest.cybersource.com"
            : "api.cybersource.com";
    }

    public IReadOnlyDictionary<string, string> ToDictionary(bool sandbox)
    {
        var environment = Environment(sandbox);

        _configurationDictionary.TryAdd("authenticationType", "HTTP_SIGNATURE");
        _configurationDictionary.TryAdd("merchantID", MerchantId);
        _configurationDictionary.TryAdd("merchantsecretKey", MerchantSecretKey);
        _configurationDictionary.TryAdd("merchantKeyId", MerchantKeyId);
        _configurationDictionary.TryAdd("runEnvironment", environment);
        _configurationDictionary.TryAdd("keyAlias", MerchantId);
        _configurationDictionary.TryAdd("keyPass", MerchantId);

        return _configurationDictionary;
    }
}
