using VirtoCommerce.OrdersModule.Core.Model;

namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourceCapturePaymentRequest : CyberSourceRequest
{
    public string Token { get; set; }
    public PaymentIn Payment { get; set; }
    public CustomerOrder Order { get; set; }
    public decimal? Amount { get; set; }
    public int PaymentNumber { get; set; }
    public bool IsFinal { get; set; }
    public string Notes { get; set; }
}
