using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Services;

namespace VirtoCommerce.CyberSourcePayment.Web.Controllers.Api;

[Authorize]
[Route("api/payments/cybersource")]
public class PaymentsController(
    ICyberSourceClient client,
    IPaymentService paymentService
) : Controller
{
    [HttpPost]
    [Route("{paymentId}")]
    [AllowAnonymous] // todo: remove

    public virtual async Task<IActionResult> RefreshPaymentStatus(string paymentId)
    {
        // TODO: user validation??
        var payment = (await paymentService.GetAsync([paymentId])).First();
        await client.RefreshPaymentStatus(payment);
        return Ok();
    }
}
