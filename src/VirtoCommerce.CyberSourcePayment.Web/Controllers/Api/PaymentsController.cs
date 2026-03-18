using System.Linq;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VirtoCommerce.CyberSourcePayment.Core.Models;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Services;

namespace VirtoCommerce.CyberSourcePayment.Web.Controllers.Api;

[Authorize]
[Route("api/payments/cybersource")]
public class PaymentsController(
    ICyberSourceClient client,
    IPaymentService paymentService,
    IOptions<CyberSourcePaymentMethodOptions> options) : Controller
{
    [HttpPost]
    [Route("refresh-payment-status/{paymentId}")]
    public virtual async Task<IActionResult> RefreshPaymentStatus(string paymentId)
    {
        var payment = (await paymentService.GetAsync([paymentId])).First();
        var result = await client.RefreshPaymentStatus(payment);
        return Ok(result);
    }

    [HttpGet]
    [Route("transaction/{transactionId}")]
    public virtual async Task<IActionResult> GetTransaction(string transactionId)
    {
        var config = new Configuration
        {
            MerchantConfigDictionaryObj = options.Value.ToDictionary(true)
        };
        var api = new TransactionDetailsApi(config);
        var result = await api.GetTransactionAsync(transactionId);
        return Ok(result);
    }
}
