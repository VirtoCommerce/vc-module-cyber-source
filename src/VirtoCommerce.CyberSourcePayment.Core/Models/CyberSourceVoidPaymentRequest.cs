using VirtoCommerce.OrdersModule.Core.Model;

namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourceVoidPaymentRequest : CyberSourceRequest
{
    public CustomerOrder Order { get; set; }
}
