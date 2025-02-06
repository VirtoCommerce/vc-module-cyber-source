namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class JwtKeyModel
{
    public string Jwt { get; set; }

    public string KeyId { get; set; }

    public string ClientLibrary { get; set; }

    public string ClientLibraryIntegrity { get; set; }
}
