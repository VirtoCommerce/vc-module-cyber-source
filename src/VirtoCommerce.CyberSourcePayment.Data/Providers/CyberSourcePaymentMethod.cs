using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CyberSource.Model;
using Newtonsoft.Json;
using VirtoCommerce.CyberSourcePayment.Core;
using VirtoCommerce.CyberSourcePayment.Core.Services;
using VirtoCommerce.OrdersModule.Core.Model;
using VirtoCommerce.PaymentModule.Core.Model;
using VirtoCommerce.PaymentModule.Model.Requests;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Settings;
using VirtoCommerce.StoreModule.Core.Model;
using CapturePaymentRequest = VirtoCommerce.PaymentModule.Model.Requests.CapturePaymentRequest;
using RefundPaymentRequest = VirtoCommerce.PaymentModule.Model.Requests.RefundPaymentRequest;
using VoidPaymentRequest = VirtoCommerce.PaymentModule.Model.Requests.VoidPaymentRequest;

namespace VirtoCommerce.CyberSourcePayment.Data.Providers;

public class CyberSourcePaymentMethod(
    ICyberSourceClient cyberSourceClient)
    : PaymentMethod(nameof(CyberSourcePaymentMethod))
{
    private bool Sandbox => Settings.GetValue<bool>(ModuleConstants.Settings.General.CyberSourceSandbox);

    public override PaymentMethodGroupType PaymentMethodGroupType => PaymentMethodGroupType.BankCard;
    public override PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;

    public override ProcessPaymentRequestResult ProcessPayment(ProcessPaymentRequest request)
    {
        return ProcessPaymentAsync(request).GetAwaiter().GetResult();
    }

    public override PostProcessPaymentRequestResult PostProcessPayment(PostProcessPaymentRequest request)
    {
        return PostProcessPaymentAsync(request).GetAwaiter().GetResult();
    }

    public override ValidatePostProcessRequestResult ValidatePostProcessRequest(NameValueCollection queryString)
    {
        return new ValidatePostProcessRequestResult
        {
            IsSuccess = true,
        };
    }

    public override CapturePaymentRequestResult CaptureProcessPayment(CapturePaymentRequest context)
    {
        throw new NotImplementedException();
    }

    public override RefundPaymentRequestResult RefundProcessPayment(RefundPaymentRequest context)
    {
        throw new NotImplementedException();
    }

    public override VoidPaymentRequestResult VoidProcessPayment(VoidPaymentRequest request)
    {
        throw new NotImplementedException();
    }

    #region protected async methods

    protected virtual async Task<ProcessPaymentRequestResult> ProcessPaymentAsync(ProcessPaymentRequest request)
    {
        var store = (Store)request.Store;

        if (store == null || (store.SecureUrl.IsNullOrEmpty() && store.Url.IsNullOrEmpty()))
        {
            throw new InvalidOperationException("The store URL is not specified.");
        }

        var url = store.SecureUrl.IsNullOrEmpty() ? store.Url : store.SecureUrl;
        var cardTypesValue = Settings.GetValue<string>(ModuleConstants.Settings.General.CyberSourceCardTypes);

        var cardTypes = cardTypesValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.IsNullOrEmpty())
            .Select(x => x.Trim().ToUpperInvariant())
            .ToArray();

        var jwtData = await cyberSourceClient.GenerateCaptureContext(Sandbox, url, cardTypes);

        var result = new ProcessPaymentRequestResult
        {
            IsSuccess = true,
            NewPaymentStatus = PaymentStatus.Pending,
            PublicParameters = new()
            {
                {"jwt", jwtData.Jwt},
                {"clientLibraryIntegrity", jwtData.ClientLibraryIntegrity},
                {"clientScript", jwtData.ClientLibrary},
                {"kid", jwtData.KeyId},
            },
        };

        var payment = (PaymentIn)request.Payment;
        payment.PaymentStatus = PaymentStatus.Pending;
        payment.Status = payment.PaymentStatus.ToString();

        return result;
    }

    protected virtual async Task<PostProcessPaymentRequestResult> PostProcessPaymentAsync(PostProcessPaymentRequest request)
    {
        var token = request.Parameters.Get("token");
        var payment = (PaymentIn)request.Payment;
        var order = (CustomerOrder)request.Order;

        var paymentResult = await cyberSourceClient.ProcessPayment(Sandbox, token, payment, order);

        var result = GetPostPaymentResult(paymentResult, payment, order);
        return result;
    }

    #endregion

    #region Post Process Results

    private static PostProcessPaymentRequestResult GetPostPaymentResult(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    ) => response.Status switch
    {
        // PtsV2PaymentsPost201Response described here
        // https://github.com/CyberSource/cybersource-rest-client-node/blob/master/docs/PtsV2PaymentsPost201Response.md
        "AUTHORIZED" => PaymentApproved(response, payment, order),
        "DECLINED" => PaymentDeclined(response, payment),

        "PARTIAL_AUTHORIZED"
            or "INVALID_REQUEST" => PaymentInvalid(response, payment),

        "PENDING_AUTHENTICATION"
            or "AUTHORIZED_PENDING_REVIEW"
            or "AUTHORIZED_RISK_DECLINED"
            or "PENDING_REVIEW" => PaymentPending(response, payment),

        _ => new PostProcessPaymentRequestResult
        {
            ErrorMessage = response.ErrorInformation?.Message
                           ?? response.ErrorInformation?.Reason,
            IsSuccess = false,
        },
    };


    private static PostProcessPaymentRequestResult PaymentApproved(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    )
    {
        var result = new PostProcessPaymentRequestResult
        {
            NewPaymentStatus = PaymentStatus.Authorized,
            OrderId = order.Id,
            OuterId = response.ProcessorInformation.TransactionId,
            IsSuccess = true,
        };

        payment.Status = result.NewPaymentStatus.ToString();
        payment.IsApproved = true;
        payment.AuthorizedDate = DateTime.UtcNow;
        payment.CapturedDate = DateTime.UtcNow;
        payment.Comment = $"Paid successfully. Transaction info {response.Id}{Environment.NewLine}";

        order.Status = "Processing";

        var transaction = new PaymentGatewayTransaction
        {
            IsProcessed = true,
            ProcessedDate = DateTime.UtcNow,
            CurrencyCode = payment.Currency,
            Amount = payment.Sum,
            Note = $"Transaction ID: {response.ProcessorInformation.TransactionId}",
            ResponseCode = response.ProcessorInformation.ResponseCode,
            ResponseData = JsonConvert.SerializeObject(response),
        };

        payment.Transactions.Add(transaction);

        return result;
    }

    private static PostProcessPaymentRequestResult PaymentDeclined(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment
    )
    {
        var transactionMessage = response.ErrorInformation.Message;
        var errorMessage = $"Your transaction was declined: {transactionMessage}";

        payment.Status = PaymentStatus.Declined.ToString();
        payment.ProcessPaymentResult = new ProcessPaymentRequestResult { ErrorMessage = errorMessage };
        payment.Comment = $"{errorMessage}{Environment.NewLine}";

        return new PostProcessPaymentRequestResult { ErrorMessage = errorMessage };
    }

    private static PostProcessPaymentRequestResult PaymentPending(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment
    )
    {
        var transactionMessage = response.ErrorInformation.Message;
        var errorMessage = $"Your transaction was held for review: {transactionMessage}";
        payment.ProcessPaymentResult = new ProcessPaymentRequestResult { ErrorMessage = errorMessage };
        payment.Comment = $"{errorMessage}{Environment.NewLine}";
        payment.OuterId = response.ProcessorInformation.TransactionId;
        payment.Status = PaymentStatus.Pending.ToString();

        return new PostProcessPaymentRequestResult { ErrorMessage = errorMessage };
    }

    private static PostProcessPaymentRequestResult PaymentInvalid(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment
    )
    {
        var transactionMessage = response.ErrorInformation.Message;
        var errorMessage = $"There was an error processing your transaction: {transactionMessage}";

        payment.Status = PaymentStatus.Error.ToString();
        payment.ProcessPaymentResult = new ProcessPaymentRequestResult { ErrorMessage = errorMessage };
        payment.Comment = $"{errorMessage}{Environment.NewLine}";

        return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };
    }

    #endregion
}
