using VirtoCommerce.OrdersModule.Core.Model;

namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourceRefundPaymentRequest : CyberSourceRequest
{
    public PaymentIn Payment { get; set; }
}
