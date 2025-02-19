using System.Threading.Tasks;
using CyberSource.Model;
using VirtoCommerce.CyberSourcePayment.Core.Models;

namespace VirtoCommerce.CyberSourcePayment.Core.Services;

public interface ICyberSourceClient
{
    Task<JwtKeyModel> GenerateCaptureContext(CyberSourceRequestContext context);
    Task<PtsV2PaymentsPost201Response> ProcessPayment(CyberSourceProcessPaymentRequest request);
    Task<PtsV2PaymentsCapturesPost201Response> CapturePayment(CyberSourceCapturePaymentRequest request);
    Task<PtsV2PaymentsRefundPost201Response> RefundPayment(CyberSourceRefundPaymentRequest request);
    Task<PtsV2PaymentsVoidsPost201Response> VoidPayment(CyberSourceVoidPaymentRequest request);
}
