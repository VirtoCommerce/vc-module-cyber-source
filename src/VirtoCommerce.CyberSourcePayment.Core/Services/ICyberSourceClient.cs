using CyberSource.Model;
using VirtoCommerce.CyberSourcePayment.Core.Models;
using VirtoCommerce.OrdersModule.Core.Model;

namespace VirtoCommerce.CyberSourcePayment.Core.Services;

public interface ICyberSourceClient
{
    JwtKeyModel GenerateCaptureContext(bool sandbox, string storeUrl, string[] cardTypes);
    PtsV2PaymentsPost201Response ProcessPayment(bool sandbox, string token, PaymentIn payment, CustomerOrder order);
}
