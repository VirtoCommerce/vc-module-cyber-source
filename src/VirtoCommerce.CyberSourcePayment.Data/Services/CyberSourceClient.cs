using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using CyberSource.Api;
using CyberSource.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using VirtoCommerce.CustomerModule.Core.Model;
using VirtoCommerce.CustomerModule.Core.Services;
using VirtoCommerce.CyberSourcePayment.Core.Models;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.Platform.Core.Security;

namespace VirtoCommerce.CyberSourcePayment.Data.Services;

public class CyberSourceClient(
    IOptions<CyberSourcePaymentMethodOptions> options,
    IMemberService memberService,
    Func<UserManager<ApplicationUser>> userManagerFactory,
    CyberSourceJwkValidator jwkValidator
    ) : ICyberSourceClient
{
    public virtual async Task<JwtKeyModel> GenerateCaptureContext(bool sandbox, string storeUrl, string[] cardTypes)
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

        if (options.Value.ValidateSignature)
        {
            await jwkValidator.VerifyJwt(sandbox, jwt);
        }

        return DecodeJwtToJwtKeyModel(jwt);
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
