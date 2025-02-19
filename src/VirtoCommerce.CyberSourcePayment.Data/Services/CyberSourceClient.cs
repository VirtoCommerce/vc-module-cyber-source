using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Client;
using CyberSource.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Polly;
using VirtoCommerce.CustomerModule.Core.Model;
using VirtoCommerce.CustomerModule.Core.Services;
using VirtoCommerce.CyberSourcePayment.Core;
using VirtoCommerce.CyberSourcePayment.Core.Models;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.OrdersModule.Core.Services;
using VirtoCommerce.Platform.Core.Security;
using VirtoCommerce.Platform.Core.Settings;
using VirtoCommerce.StoreModule.Core.Services;

namespace VirtoCommerce.CyberSourcePayment.Data.Services;

public class CyberSourceClient(
    IOptions<CyberSourcePaymentMethodOptions> options,
    IMemberService memberService,
    ICustomerOrderService orderService,
    IStoreService storeService,
    ISettingsManager settingsManager,
    Func<UserManager<ApplicationUser>> userManagerFactory,
    CyberSourceJwkValidator jwkValidator
    ) : ICyberSourceClient
{
    public virtual async Task<JwtKeyModel> GenerateCaptureContext(CyberSourceRequestContext context)
    {
        var policy = Policy
            .Handle<SecurityTokenException>()
            .Or<SecurityTokenMalformedException>()
            .WaitAndRetryAsync(options.Value.ValidateSignatureRetryCount, _ => TimeSpan.FromMilliseconds(500));

        var result = await policy.ExecuteAsync(async () =>
            await GenerateCaptureContextInternal(context));

        return result;
    }

    protected virtual async Task<JwtKeyModel> GenerateCaptureContextInternal(CyberSourceRequestContext context)
    {
        var request = new GenerateCaptureContextRequest(
            "v2.0",
            [context.StoreUrl],
            context.CardTypes.ToList()
        );
        var config = CreateConfiguration(context.Sandbox);
        var api = new MicroformIntegrationApi(config);
        var jwt = await api.GenerateCaptureContextAsync(request);

        await jwkValidator.VerifyJwt(context.Sandbox, jwt);

        return DecodeJwtToJwtKeyModel(jwt);
    }

    public virtual async Task<PtsV2PaymentsPost201Response> ProcessPayment(CyberSourceProcessPaymentRequest request)
    {
        var processRequest = await GeneratePaymentRequest(request);

        processRequest.TokenInformation ??= new Ptsv2paymentsTokenInformation
        {
            TransientTokenJwt = request.Token,
        };

        var config = CreateConfiguration(request.Sandbox);
        var api = new PaymentsApi(config);
        var result = await api.CreatePaymentAsync(processRequest);

        return result;
    }

    public virtual async Task<PtsV2PaymentsCapturesPost201Response> CapturePayment(CyberSourceCapturePaymentRequest request)
    {
        var config = CreateConfiguration(request.Sandbox);
        var api = new CaptureApi(config);

        var orderInfo = await GetCaptureOrderInfo(request);
        var captureRequest = new CapturePaymentRequest
        {
            OrderInformation = orderInfo,
            ProcessingInformation = new Ptsv2paymentsidcapturesProcessingInformation
            {
                CaptureOptions = new Ptsv2paymentsidcapturesProcessingInformationCaptureOptions
                {
                    CaptureSequenceNumber = request.PaymentNumber,
                    TotalCaptureCount = request.IsFinal ? request.PaymentNumber : request.PaymentNumber + 1,
                    IsFinal = request.IsFinal.ToString().ToLowerInvariant(),
                    Notes = request.Notes,
                }
            }
        };

        var result = await api.CapturePaymentAsync(captureRequest, request.OuterPaymentId);
        return result;
    }

    public virtual async Task<PtsV2PaymentsRefundPost201Response> RefundPayment(CyberSourceRefundPaymentRequest request)
    {
        var config = CreateConfiguration(request.Sandbox);
        var api = new RefundApi(config);

        var refundRequest = new RefundPaymentRequest
        {
            OrderInformation = new Ptsv2paymentsidrefundsOrderInformation
            {
                AmountDetails = new Ptsv2paymentsidcapturesOrderInformationAmountDetails
                {
                    TotalAmount = request.Payment.Total.ToString(CultureInfo.InvariantCulture),
                },
            },
        };

        var result = await api.RefundPaymentAsync(refundRequest, request.OuterPaymentId);
        return result;
    }

    public virtual async Task<PtsV2PaymentsVoidsPost201Response> VoidPayment(CyberSourceVoidPaymentRequest request)
    {
        var config = CreateConfiguration(request.Sandbox);
        var api = new VoidApi(config);

        var voidRequest = new VoidPaymentRequest
        {
            ClientReferenceInformation = new Ptsv2paymentsidreversalsClientReferenceInformation
            {
                Code = request.Payment.CustomerId
            }
        };

        var result = await api.VoidPaymentAsync(voidRequest, request.OuterPaymentId);
        return result;
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

    protected virtual async Task<CreatePaymentRequest> GeneratePaymentRequest(CyberSourceProcessPaymentRequest request)
    {
        var result = new CreatePaymentRequest(
            OrderInformation: await GetOrderInfo(request.Payment, request.Order),
            PaymentInformation: new Ptsv2paymentsPaymentInformation(),
            ProcessingInformation: new Ptsv2paymentsProcessingInformation
            {
                Capture = request.SingleMessageMode,
            }
        );

        return result;
    }

    protected virtual async Task<Ptsv2paymentsOrderInformation> GetOrderInfo(PaymentIn payment, CustomerOrder order)
    {
        using var userManager = userManagerFactory();
        var user = await userManager.FindByIdAsync(order.CustomerId);

        if (user == null)
        {
            throw new InvalidOperationException($"User with id {order.CustomerId} not found");
        }

        var contact = (Contact)(await memberService.GetByIdAsync(user.MemberId));
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

    public async Task<PtsV2PaymentsPost201Response1> RefreshPaymentStatus(PaymentIn payment, string outerId)
    {
        var order = (await orderService.GetAsync([payment.OrderId])).First();
        var store = (await storeService.GetAsync([order.StoreId])).First();
        await settingsManager.DeepLoadSettingsAsync(store);
        var sandbox = store.Settings.GetValue<bool>(ModuleConstants.Settings.General.Sandbox);

        var config = CreateCyberSourceClientConfig(sandbox);
        var api = new PaymentsApi(config);

        var request = new RefreshPaymentStatusRequest
        {
            ProcessingInformation = new Ptsv2refreshpaymentstatusidProcessingInformation(),
            AgreementInformation = new Ptsv2refreshpaymentstatusidAgreementInformation(),
            ClientReferenceInformation = new Ptsv2refreshpaymentstatusidClientReferenceInformation
            {
                Code = payment.CustomerId
            },
            PaymentInformation = new Ptsv2refreshpaymentstatusidPaymentInformation
            {
                Customer = new Ptsv2refreshpaymentstatusidPaymentInformationCustomer
                {
                    CustomerId = payment.CustomerId
                },
                PaymentType = new Ptsv2refreshpaymentstatusidPaymentInformationPaymentType()
            }
        };

        try
        {
            var result = await api.RefreshPaymentStatusAsync(outerId ?? payment.OuterId, request);

            //var api = new TransactionDetailsApi(config);
            //var result = await api.GetTransactionAsync(outerId ?? payment.OuterId);


            return result;
        }
        catch (ApiException ex)
        {
            throw new InvalidOperationException($"Error refreshing payment status: {ex.Message}", ex);
        }
    }

    protected virtual Configuration CreateCyberSourceClientConfig(bool sandbox)
    {
        return new Configuration
        {
            MerchantConfigDictionaryObj = options.Value.ToDictionary(sandbox), // todo: SANDBOX
        };
    }

    protected virtual async Task<Ptsv2paymentsidcapturesOrderInformation> GetCaptureOrderInfo(CyberSourceCapturePaymentRequest request)
    {
        using var userManager = userManagerFactory();
        var user = await userManager.FindByIdAsync(request.Order.CustomerId);

        if (user == null)
        {
            throw new InvalidOperationException($"User with id {request.Order.CustomerId} not found");
        }

        var contact = (Contact)(await memberService.GetByIdAsync(user.MemberId));
        var email = contact.Emails.FirstOrDefault()
                    ?? contact.SecurityAccounts.Select(x => x.Email).FirstOrDefault();

        var payment = request.Payment;
        var order = request.Order;

        var result = new Ptsv2paymentsidcapturesOrderInformation
        {
            BillTo = new Ptsv2paymentsidcapturesOrderInformationBillTo
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
            AmountDetails = new Ptsv2paymentsidcapturesOrderInformationAmountDetails
            {
                //DiscountAmount = order.DiscountAmount.ToString(CultureInfo.InvariantCulture),
                //TaxAmount = order.TaxTotal.ToString(CultureInfo.InvariantCulture),
                TotalAmount = (request.Amount ?? order.Total).ToString(CultureInfo.InvariantCulture),
                Currency = order.Currency,
            },
        };

        return result;
    }

    protected virtual Configuration CreateConfiguration(bool sandbox)
    {
        return new Configuration
        {
            MerchantConfigDictionaryObj = options.Value.ToDictionary(sandbox),
        };
    }
}
