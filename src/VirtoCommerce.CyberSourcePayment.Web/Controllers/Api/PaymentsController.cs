using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.CyberSourcePayment.Data.Services;

namespace VirtoCommerce.CyberSourcePayment.Web.Controllers.Api;

[Authorize]
[Route("api/payments/cybersource")]
public class PaymentsController(
    ICyberSourceClient client
) : Controller
{
    [HttpGet]
    [HttpPost]
    [HttpPatch]
    [HttpPut]
    [HttpOptions]
    [AllowAnonymous]
    [Route("changed")]
    public async Task<ActionResult> DecisionChanged()
    {
        var result = await Task.FromResult(12);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet]
    [Route("health-check")]
    public ActionResult HealthCheck()
    {
        return Ok(new { status = "ok" });
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("unregister-webhooks")]
    public async Task<ActionResult> UnregisterWebhooks()
    {
        await client.UnregisterWebhook();
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("register-webhooks")]
    public async Task<ActionResult> RegisterWebhooks()
    {
        await client.RegisterWebhook();
        //var config = new CyberSource.Client.Configuration
        //{
        //    MerchantConfigDictionaryObj = options.Value.ToDictionary(true),
        //};
        //var webhookApi = new CreateNewWebhooksApi(config);

        //var request = new CreateWebhookRequest
        //{
        //    Name = "Custom Webhook",
        //    Description = "Custom Webhook description",
        //    EventTypes = ["risk.casemanagement.decision.accept", "risk.casemanagement.decision.reject"],
        //    HealthCheckUrl = "https://cfd0-176-79-145-242.ngrok-free.app/api/payments/cybersource/health-check",
        //    //NotificationScope = "SELF",
        //    OrganizationId = "virtocommerce_1729579220",
        //    ProductId = "fraudManagementEssentials",
        //    RetryPolicy = new Notificationsubscriptionsv1webhooksRetryPolicy
        //    {
        //        Algorithm = "ARITHMETIC",
        //        FirstRetry = 1,
        //        Interval = 1,
        //        NumberOfRetries = 3,
        //        DeactivateFlag = "false",
        //        RepeatSequenceCount = 0,
        //        RepeatSequenceWaitTime = 0,
        //    },
        //    SecurityPolicy = new Notificationsubscriptionsv1webhooksSecurityPolicy1
        //    {
        //        SecurityType = "KEY",
        //        ProxyType = "external"
        //    },
        //    WebhookUrl = "https://cfd0-176-79-145-242.ngrok-free.app/api/payments/cybersource/approve",
        //};

        //await webhookApi.CreateWebhookSubscriptionAsync(request);

        //var result = await Task.FromResult(12);
        //return Ok(result);
        return Ok();
    }

    [HttpPost]
    [AllowAnonymous]
    [Route("list")]
    public async Task<ActionResult> GetProducts()
    {
        var products = await ((CyberSourceClient)client).GetProductsList();
        var webhooks = await ((CyberSourceClient)client).GetWebhooksList();
        return Ok(new { products, webhooks });
    }
}
