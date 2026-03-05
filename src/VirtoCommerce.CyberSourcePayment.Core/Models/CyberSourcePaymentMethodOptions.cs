using System;
using System.Collections.Generic;

namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourcePaymentMethodOptions
{
    public string MerchantId { get; set; }
    public string MerchantKeyId { get; set; }
    public string MerchantSecretKey { get; set; }

    public int ValidateSignatureRetryCount { get; set; } = 1;

    public static string Environment(bool sandbox)
    {
        return sandbox
            ? "apitest.cybersource.com"
            : "api.cybersource.com";
    }

    public IReadOnlyDictionary<string, string> ToDictionary(bool sandbox)
    {
        if (string.IsNullOrEmpty(MerchantId) || string.IsNullOrEmpty(MerchantKeyId) || string.IsNullOrEmpty(MerchantSecretKey))
        {
            throw new InvalidOperationException(
                "CyberSource payment configuration is incomplete. " +
                "Please provide MerchantId, MerchantKeyId, and MerchantSecretKey in the 'Payments:CyberSource' configuration section.");
        }

        var environment = Environment(sandbox);

        return new Dictionary<string, string>
        {
            ["authenticationType"] = "HTTP_SIGNATURE",
            ["merchantID"] = MerchantId,
            ["merchantsecretKey"] = MerchantSecretKey,
            ["merchantKeyId"] = MerchantKeyId,
            ["runEnvironment"] = environment,
            ["keyAlias"] = MerchantId,
            ["keyPass"] = MerchantId,
            ["enableClientCert"] = "false",
            ["useMetaKey"] = "false",
        };
    }
}
