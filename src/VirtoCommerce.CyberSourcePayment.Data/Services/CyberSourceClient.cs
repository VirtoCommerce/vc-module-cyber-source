using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Client;
using CyberSource.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using VirtoCommerce.CustomerModule.Core.Model;
using VirtoCommerce.CustomerModule.Core.Services;
using VirtoCommerce.CyberSourcePayment.Core.Models;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.CyberSourcePayment.Data.Services;

public class CyberSourceClient(
    IOptions<CyberSourcePaymentMethodOptions> options,
    IMemberService memberService,
    Func<UserManager<ApplicationUser>> userManagerFactory,
    CyberSourceJwkValidator jwkValidator,
    IHttpContextAccessor httpContextAccessor
    ) : ICyberSourceClient
{
    private const string _webhookName = "VirtoCommerce Webhook";
    //private const string _cyberSourceProductId = "fraudManagementEssentials";
    //private const string _cyberSourceProductId = "cardProcessing";
    private const string _cyberSourceProductId = "decisionManager";
    private const string _cyberSourceAcceptEventName = "risk.casemanagement.decision.accept";
    private const string _cyberSourceRejectEventName = "risk.casemanagement.decision.reject";
    //private const string _cyberSourceAcceptEventName = "payments.credits.accept";

    public virtual async Task<JwtKeyModel> GenerateCaptureContext(bool sandbox, string storeUrl, string[] cardTypes)
    {
        var retryCount = options.Value.ValidateSignatureRetryCount;
        while (true)
        {
            try
            {
                var request = new GenerateCaptureContextRequest(
                    "v2.0",
                    [storeUrl],
                    cardTypes.ToList()
                );
                var config = new CyberSource.Client.Configuration
                {
                    MerchantConfigDictionaryObj = options.Value.ToDictionary(sandbox),
                };
                var api = new MicroformIntegrationApi(config);
                var jwt = await api.GenerateCaptureContextAsync(request);

                if (retryCount > 0)
                {
                    await jwkValidator.VerifyJwt(sandbox, jwt);
                }
                return DecodeJwtToJwtKeyModel(jwt);
            }
            catch (Exception exception)
            #region exceptions
            //catch (SecurityTokenMalformedException)
            //catch (SecurityTokenException)
            //catch (SecurityTokenDecryptionFailedException)
            //catch (SecurityTokenEncryptionKeyNotFoundException)
            //catch (SecurityTokenValidationException)
            //catch (SecurityTokenExpiredException)
            //catch (SecurityTokenInvalidAudienceException)
            //catch (SecurityTokenInvalidLifetimeException)
            //catch (SecurityTokenInvalidSignatureException)
            //catch (SecurityTokenNoExpirationException)
            //catch (SecurityTokenNotYetValidException)
            //catch (SecurityTokenReplayAddFailedException)
            //catch (SecurityTokenReplayDetectedException)
            #endregion
            {
                if (exception is SecurityTokenException || exception is SecurityTokenMalformedException)
                {
                    retryCount--;
                    if (retryCount > 0)
                    {
                        continue;
                    }
                }

                throw;
            }
        }
    }

    public static JwtKeyModel DecodeJwtToJwtKeyModel(string jwt)
    {
        var jwtKeyModel = new JwtKeyModel { Jwt = jwt };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);

        // Extract the 'ctx' claim
        var ctxClaim = token.Claims.FirstOrDefault(c => c.Type == "ctx")?.Value;
        if (ctxClaim == null)
        {
            throw new InvalidOperationException("The ctx claim is missing in the JWT.");
        }

        // Extract the 'flx' claim
        var flxClaim = token.Claims.FirstOrDefault(c => c.Type == "flx")?.Value;
        if (flxClaim == null)
        {
            throw new InvalidOperationException("The flx claim is missing in the JWT.");
        }

        // Parse the ctx claim as JSON
        var ctxObject = JObject.Parse(ctxClaim);
        var flxObject = JObject.Parse(flxClaim);

        jwtKeyModel.ClientLibrary = ctxObject["data"]?["clientLibrary"]?.ToString();
        jwtKeyModel.ClientLibraryIntegrity = ctxObject["data"]?["clientLibraryIntegrity"]?.ToString();
        jwtKeyModel.KeyId = flxObject["jwk"]?["kid"]?.ToString();

        return jwtKeyModel;
    }

    public virtual async Task<PtsV2PaymentsPost201Response> ProcessPayment(bool sandbox, string token, PaymentIn payment, CustomerOrder order)
    {
        using var userManager = userManagerFactory();
        var user = await userManager.FindByIdAsync(order.CustomerId);
        if (user == null)
        {
            throw new InvalidOperationException($"User with id {order.CustomerId} not found");
        }
        var contact = (Contact)(await memberService.GetByIdAsync(user.MemberId));

        var request = GeneratePaymentRequest(payment, order, contact);

        request.TokenInformation ??= new Ptsv2paymentsTokenInformation
        {
            TransientTokenJwt = token
        };

        var config = new CyberSource.Client.Configuration
        {
            MerchantConfigDictionaryObj = options.Value.ToDictionary(sandbox),
        };
        var api = new PaymentsApi(config);
        var result = await api.CreatePaymentAsync(request);
        return result;
    }

    public virtual async Task RegisterWebhook()
    {
        try
        {
            var webhooks = await GetWebhooksList();
            if (webhooks.Any(x => x.Name == _webhookName))
            {
                await UnregisterWebhook();
                //return;
            }
        }
        catch (ApiException ex)
        {
            if (ex.ErrorCode != 404)
            {
                throw;
            }
        }

        var config = CreateCyberSourceClientConfig();
        var webhookApi = new CreateNewWebhooksApi(config);
        var webhookUrl = GetWebhookUrl();
        var request = new CreateWebhookRequest
        {
            Name = _webhookName,
            Description = "This webhook integrates the VirtoCommerce platform with the CyberSource payment gateway, enabling real-time notifications when transactions are flagged for manual review through CyberSource's Fraud Management Essentials system. By promptly alerting managers to pending reviews, it streamlines the fraud evaluation process and helps maintain secure, efficient payment workflows.",
            ProductId = _cyberSourceProductId,
            EventTypes = AllEvents(), // _cyberSourceRejectEventName],
            HealthCheckUrl = $"{webhookUrl}/api/payments/cybersource/health-check",
            WebhookUrl = $"{webhookUrl}/api/payments/cybersource/changed",
            // NotificationScope = "DESCENDENTS",
            OrganizationId = options.Value.MerchantId,
            RetryPolicy = new Notificationsubscriptionsv1webhooksRetryPolicy
            {
                Algorithm = "ARITHMETIC",
                FirstRetry = 1,
                Interval = 1,
                NumberOfRetries = 3,
                DeactivateFlag = "false",
                RepeatSequenceCount = 0,
                RepeatSequenceWaitTime = 0,
            },
            SecurityPolicy = new Notificationsubscriptionsv1webhooksSecurityPolicy1
            {
                SecurityType = "KEY",
                ProxyType = "external"
            },
        };
        try
        {
            var result = await webhookApi.CreateWebhookSubscriptionAsync(request);
            //if (result.Status != "ACTIVE")
            //{
            //    await ActivateWebhook(result.WebhookId);
            //}
        }
        catch (ApiException ex)
        {
            if (!ex.Message.Contains("Notificationsubscriptionsv1webhooksNotificationScope"))
            {
                throw;
            }
        }

    }

    //public virtual async Task ActivateWebhook(string webhookId)
    //{
    //    var config = CreateCyberSourceClientConfig();
    //    var manageApi = new ManageWebhooksApi(config);
    //    var activateRequest = new UpdateWebhookRequest
    //    {
    //        EventTypes = [_cyberSourceAcceptEventName], // _cyberSourceRejectEventName],
    //        ProductId = _cyberSourceProductId,
    //        Status = "ACTIVE",
    //        OrganizationId = options.Value.MerchantId,

    //    };
    //    await manageApi.UpdateWebhookSubscriptionAsync(webhookId, activateRequest);
    //}

    protected virtual string GetWebhookUrl()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request == null)
        {
            throw new InvalidOperationException("HttpContext is not available.");
        }
        return options.Value.ProxyWebhookDomain.IsNullOrEmpty()
            ? $"{request.Scheme}://{request.Host}"
            : options.Value.ProxyWebhookDomain;
    }

    public virtual async Task UnregisterWebhook() // todo: create internal method to unregister by id
    {
        try
        {
            var allWebhooks = await GetWebhooksList();
            var webhooks = allWebhooks.Where(x => x.Name == _webhookName);
            if (!webhooks.Any())
            {
                return;
            }

            foreach (var webhook in webhooks)
            {
                var config = CreateCyberSourceClientConfig();
                var webhookApi = new ManageWebhooksApi(config);
                await webhookApi.DeleteWebhookSubscriptionAsync(webhook.WebhookId);
            }
        }
        catch (ApiException ex)
        {
            if (ex.ErrorCode == 404)
            {
                // Webhook not found
            }
            else
            {
                throw;
            }
        }
    }

    public virtual async Task<List<InlineResponse2003>> GetWebhooksList()
    {
        try
        {
            var allSubscriptions = new List<InlineResponse2003>();

            var events = AllEvents();
            foreach (var @event in events)
            {
                try
                {
                    var config = CreateCyberSourceClientConfig();
                    var api = new ManageWebhooksApi(config);
                    var result = await api.GetWebhookSubscriptionsByOrgAsync(
                        options.Value.MerchantId,
                        _cyberSourceProductId,
                        @event
                    );
                    allSubscriptions.AddRange(result);
                }
                catch (ApiException ex)
                {
                    if (ex.ErrorCode == 404)
                    {
                        continue;
                    }
                    throw;
                }
            }

            return allSubscriptions.DistinctBy(x => x.WebhookId).ToList();
        }
        catch (ApiException ex)
        {
            if (ex.ErrorCode == 404)
            {
                return new List<InlineResponse2003>();
            }
            throw;
        }
    }
    //public virtual async Task<List<InlineResponse2003>> GetWebhooksList1()
    //{
    //    try
    //    {
    //        var config = CreateCyberSourceClientConfig();
    //        //var api = new ManageWebhooksApi(config);
    //        //var result = await api. GetWebhookSubscriptionsByOrgAsync(
    //        //    options.Value.MerchantId
    //        //    );

    //        var api = new CyberSource.Api.ReplayWebhooksApi(config);
    //        var result = await api.

    //        return result;
    //    }
    //    catch (ApiException ex)
    //    {
    //        if (ex.ErrorCode == 404)
    //        {
    //            return new List<InlineResponse2003>();
    //        }
    //        throw;
    //    }
    //}

    public virtual async Task<List<InlineResponse2002>> GetProductsList()
    {
        var config = CreateCyberSourceClientConfig();
        var api = new CreateNewWebhooksApi(config);
        var result = await api.FindProductsToSubscribeAsync(options.Value.MerchantId);
        return result;
    }

    protected virtual List<string> AllEvents()
    {
        //return
        //    [
        //        "payments.payments.accept",
        //        "payments.payments.review",
        //        "payments.payments.reject",
        //        "payments.payments.partial.approval",
        //        "payments.reversals.accept",
        //        "payments.reversals.reject",
        //        "payments.captures.accept",
        //        "payments.captures.review",
        //        "payments.captures.reject",
        //        "payments.refunds.accept",
        //        "payments.refunds.reject",
        //        "payments.refunds.partial.approval",
        //        "payments.credits.accept",
        //        "payments.credits.review",
        //        "payments.credits.reject",
        //        "payments.credits.partial.approval",
        //        "payments.voids.accept",
        //        "payments.voids.reject",
        //    ];
        /*
        return
        [
            "risk.profile.decision.review",
            "risk.profile.decision.reject",
            "risk.profile.decision.monitor",
            "risk.casemanagement.addnote",
            "risk.casemanagement.decision.accept",
            "risk.casemanagement.decision.reject",
            //"risk.profile.decision.review.5m",
            //"risk.profile.decision.reject.5m",
            //"risk.profile.decision.monitor.5m",
            //"risk.profile.decision.review.5m",
            //"risk.profile.decision.reject.5m",
            //"risk.profile.decision.monitor.5m",
        ];
        /**/
        return
        [
            _cyberSourceAcceptEventName,
            _cyberSourceRejectEventName,
        ];

    }

    protected virtual Configuration CreateCyberSourceClientConfig()
    {
        return new CyberSource.Client.Configuration
        {
            MerchantConfigDictionaryObj = options.Value.ToDictionary(true), // todo: SANDBOX
        };
    }

    protected virtual CreatePaymentRequest GeneratePaymentRequest(PaymentIn payment, CustomerOrder order, Contact contact)
    {
        var result = new CreatePaymentRequest(
            OrderInformation: GetOrderInfo(payment, order, contact),
            PaymentInformation: new Ptsv2paymentsPaymentInformation()
        );
        return result;
    }

    private static Ptsv2paymentsOrderInformation GetOrderInfo(PaymentIn payment, CustomerOrder order, Contact contact)
    {
        var email = contact.Emails.FirstOrDefault()
                    ?? contact.SecurityAccounts.Select(x => x.Email).FirstOrDefault();

        var result = new Ptsv2paymentsOrderInformation
        {
            BillTo = new Ptsv2paymentsOrderInformationBillTo
            {
                Locality = payment.BillingAddress.City,
                LastName = contact.LastName,
                FirstName = contact.FirstName,
                MiddleName = contact.MiddleName,
                Email = email,
                Address1 = payment.BillingAddress.Line1,
                Address2 = payment.BillingAddress.Line2,
                Country = payment.BillingAddress.CountryName,
                AdministrativeArea = payment.BillingAddress.RegionName,
                PostalCode = payment.BillingAddress.PostalCode,
            },
            LineItems = order.Items.Select(x => new Ptsv2paymentsOrderInformationLineItems
            {
                ProductName = x.Name,
                ProductSku = x.Sku,
                ProductCode = x.Id,
                DiscountAmount = x.DiscountAmount.ToString(CultureInfo.InvariantCulture),
                TaxAmount = x.TaxTotal.ToString(CultureInfo.InvariantCulture),
                TotalAmount = x.PlacedPrice.ToString(CultureInfo.InvariantCulture),
                UnitPrice = x.Price.ToString(CultureInfo.InvariantCulture),
                Gift = x.IsGift,
                Quantity = x.Quantity,
            }).ToList(),
            AmountDetails = new Ptsv2paymentsOrderInformationAmountDetails
            {
                DiscountAmount = order.DiscountAmount.ToString(CultureInfo.InvariantCulture),
                TaxAmount = order.TaxTotal.ToString(CultureInfo.InvariantCulture),
                TotalAmount = order.Total.ToString(CultureInfo.InvariantCulture),
                Currency = order.Currency,
            },
        };

        return result;
    }

}
