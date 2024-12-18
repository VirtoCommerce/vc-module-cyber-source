using System;
using System.Globalization;
using System.Linq;
using CyberSource.Api;
using CyberSource.Model;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
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
    UserManager<ApplicationUser> userManager
    ) : ICyberSourceClient
{
    public virtual JwtKeyModel GenerateCaptureContext(bool sandbox, string storeUrl, string[] cardTypes)
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
            var result = api.GenerateCaptureContext(request);
            return new JwtKeyModel { KeyId = result };
        }
        catch (Exception ex)
        {
            throw new Exception("Error generating JWT", ex);
        }
    }

    public virtual PtsV2PaymentsPost201Response ProcessPayment(bool sandbox, string token, PaymentIn payment, CustomerOrder order)
    {
        try
        {
            var user = userManager.FindByIdAsync(order.CustomerId).Result;
            var contact = (Contact)memberService.GetByIdAsync(user.MemberId).Result;

            var request = GeneratePaymentRequest(payment, order, contact);

            if (request.TokenInformation == null)
            {
                request.TokenInformation = new Ptsv2paymentsTokenInformation
                {
                    TransientTokenJwt = token
                };
            }

            var config = new CyberSource.Client.Configuration
            {
                MerchantConfigDictionaryObj = options.Value.ToDictionary(sandbox),
            };
            var api = new PaymentsApi(config);
            var result = api.CreatePayment(request);
            return result;
        }
        catch (Exception ex)
        {
            throw new Exception("Error processing payment", ex);
        }
    }

    protected virtual CreatePaymentRequest GeneratePaymentRequest(PaymentIn payment, CustomerOrder order, Contact contact)
    {
        var result = new CreatePaymentRequest(
            OrderInformation: GetOrderInfo(payment, order, contact),
            PaymentInformation: GetPaymentInfo(payment, order, contact)
        );
        return result;
    }

    private Ptsv2paymentsPaymentInformation GetPaymentInfo(PaymentIn payment, CustomerOrder order, Contact contact)
    {
        var result = new Ptsv2paymentsPaymentInformation { };
        return result;
    }

    private static Ptsv2paymentsOrderInformation GetOrderInfo(PaymentIn payment, CustomerOrder order, Contact contact)
    {
        var email = contact.Emails.FirstOrDefault();
        if (email == null)
        {
            email = contact.SecurityAccounts.Select(x => x.Email).FirstOrDefault();
        }

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
