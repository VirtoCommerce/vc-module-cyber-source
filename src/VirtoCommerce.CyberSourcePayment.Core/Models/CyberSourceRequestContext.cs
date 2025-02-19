namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourceRequestContext : CyberSourceRequest
{
    public string StoreUrl { get; set; }
    public string[] CardTypes { get; set; }
}
