using VirtoCommerce.OrdersModule.Core.Model;

namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourceProcessPaymentRequest : CyberSourceRequest
{
    public string Token { get; set; }
    public PaymentIn Payment { get; set; }
    public CustomerOrder Order { get; set; }
}
