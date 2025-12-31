using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using CyberSource.Model;
using Newtonsoft.Json;
using VirtoCommerce.CyberSourcePayment.Core;
using VirtoCommerce.CyberSourcePayment.Core.Models;
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
    : PaymentMethod(nameof(CyberSourcePaymentMethod)), ISupportCaptureFlow, ISupportRefundFlow
{
    private bool Sandbox => Settings.GetValue<bool>(ModuleConstants.Settings.General.Sandbox);
    private bool SingleMessageMode => Settings.GetValue<string>(ModuleConstants.Settings.General.PaymentMode) == ModuleConstants.Settings.SingleMessageMode;

    public override PaymentMethodGroupType PaymentMethodGroupType => PaymentMethodGroupType.BankCard;
    public override PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;
    public override bool AllowCartPayment => true;

    #region overrides

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
        return CaptureProcessPaymentAsync(context).GetAwaiter().GetResult();
    }

    public override RefundPaymentRequestResult RefundProcessPayment(RefundPaymentRequest context)
    {
        return RefundProcessPaymentAsync(context).GetAwaiter().GetResult();
    }

    public override VoidPaymentRequestResult VoidProcessPayment(VoidPaymentRequest request)
    {
        return VoidProcessPaymentAsync(request).GetAwaiter().GetResult();
    }

    #endregion

    #region protected async methods

    protected virtual async Task<ProcessPaymentRequestResult> ProcessPaymentAsync(ProcessPaymentRequest request)
    {
        var store = (Store)request.Store;

        if (store == null || (store.SecureUrl.IsNullOrEmpty() && store.Url.IsNullOrEmpty()))
        {
            throw new InvalidOperationException("The store URL is not specified.");
        }

        var url = store.SecureUrl.IsNullOrEmpty() ? store.Url : store.SecureUrl;
        var generateCaptureContext = PrepareGenerateCaptureContext(url);
        var jwtData = await cyberSourceClient.GenerateCaptureContext(generateCaptureContext);

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

        if (payment != null)
        {
            payment.PaymentStatus = PaymentStatus.Pending;
            payment.Status = payment.PaymentStatus.ToString();
        }

        return result;
    }

    protected virtual async Task<PostProcessPaymentRequestResult> PostProcessPaymentAsync(PostProcessPaymentRequest request)
    {
        var token = request.Parameters.Get("token");
        var payment = (PaymentIn)request.Payment;
        var order = (CustomerOrder)request.Order;

        var clientRequest = PrepareProcessPaymentRequest(token, payment, order);

        var paymentResult = await cyberSourceClient.ProcessPayment(clientRequest);

        var result = GetPostPaymentResult(paymentResult, payment, order);
        return result;
    }

    protected virtual async Task<CapturePaymentRequestResult> CaptureProcessPaymentAsync(CapturePaymentRequest context)
    {
        var captureRequest = PrepareCapturePaymentRequest(context);
        var captureResult = await cyberSourceClient.CapturePayment(captureRequest);

        if (captureResult.Status != CyberSourceRequest.PaymentStatus.Pending
            && captureResult.Status != CyberSourceRequest.PaymentStatus.Transmitted)
        {
            throw new InvalidOperationException($"{captureResult.Status}: {captureResult.ProcessorInformation.ProviderResponse})");
        }

        var result = new CapturePaymentRequestResult
        {
            IsSuccess = true,
            NewPaymentStatus = captureRequest.IsFinal ? PaymentStatus.Paid : PaymentStatus.Authorized,
        };

        var payment = (PaymentIn)context.Payment;

        payment.PaymentStatus = result.NewPaymentStatus;
        payment.Status = result.NewPaymentStatus.ToString();
        payment.IsApproved = true;
        payment.CapturedDate = DateTime.UtcNow;

        return result;
    }

    protected virtual async Task<RefundPaymentRequestResult> RefundProcessPaymentAsync(RefundPaymentRequest context)
    {
        var payment = (PaymentIn)context.Payment;
        var refundRequest = PrepareRefundPaymentRequest(payment, context);
        var refundResult = await cyberSourceClient.RefundPayment(refundRequest);

        var result = new RefundPaymentRequestResult();

        if (refundResult.Status == CyberSourceRequest.PaymentStatus.Pending)
        {
            result.IsSuccess = true;
            result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Refunded;
            payment.Status = result.NewPaymentStatus.ToString();
            payment.VoidedDate = DateTime.UtcNow;
        }

        return result;
    }

    protected virtual async Task<VoidPaymentRequestResult> VoidProcessPaymentAsync(VoidPaymentRequest request)
    {
        var payment = (PaymentIn)request.Payment;
        var transactionRequest = PrepareVoidPaymentRequest(payment);
        var voidResult = await cyberSourceClient.VoidPayment(transactionRequest);

        var result = new VoidPaymentRequestResult();

        if (voidResult.Status == CyberSourceRequest.PaymentStatus.Voided
             || voidResult.Status == CyberSourceRequest.PaymentStatus.Cancelled)
        {
            result.IsSuccess = true;
            result.NewPaymentStatus = payment.PaymentStatus = PaymentStatus.Voided;

            payment.IsCancelled = true;
            payment.Status = PaymentStatus.Voided.ToString();
            payment.VoidedDate = payment.CancelledDate = DateTime.UtcNow;
        }

        return result;
    }

    #endregion

    #region prepare requests

    protected virtual CyberSourceRequestContext PrepareGenerateCaptureContext(string url)
    {
        var cardTypesValue = Settings.GetValue<string>(ModuleConstants.Settings.General.CardTypes);

        var cardTypes = cardTypesValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.IsNullOrEmpty())
            .Select(x => x.Trim().ToUpperInvariant())
            .ToArray();

        var result = AbstractTypeFactory<CyberSourceRequestContext>.TryCreateInstance();

        result.Sandbox = Sandbox;
        result.CardTypes = cardTypes;
        result.StoreUrl = url;

        return result;
    }

    protected virtual CyberSourceProcessPaymentRequest PrepareProcessPaymentRequest(string token, PaymentIn payment, CustomerOrder order)
    {
        var result = AbstractTypeFactory<CyberSourceProcessPaymentRequest>.TryCreateInstance();

        result.Token = token;
        result.Payment = payment;
        result.Order = order;
        result.Sandbox = Sandbox;
        result.SingleMessageMode = SingleMessageMode;

        return result;
    }

    protected virtual CyberSourceCapturePaymentRequest PrepareCapturePaymentRequest(CapturePaymentRequest context)
    {
        var result = AbstractTypeFactory<CyberSourceCapturePaymentRequest>.TryCreateInstance();

        var payment = (PaymentIn)context.Payment;
        var order = (CustomerOrder)context.Order;

        result.OuterPaymentId = context.OuterId ?? payment.OuterId;
        result.Sandbox = Sandbox;
        result.Payment = payment;
        result.Order = order;
        result.Amount = context.CaptureAmount;
        result.PaymentNumber = payment.Captures.Count + 1;
        result.IsFinal = context.Parameters["CloseTransaction"]?.ToLowerInvariant() == "true";
        result.Notes = context.Parameters["CaptureDetails"];

        return result;
    }

    protected virtual CyberSourceRefundPaymentRequest PrepareRefundPaymentRequest(PaymentIn payment, RefundPaymentRequest context)
    {
        var result = AbstractTypeFactory<CyberSourceRefundPaymentRequest>.TryCreateInstance();

        result.OuterPaymentId = payment.OuterId;
        result.Sandbox = Sandbox;
        result.Payment = payment;
        result.Amount = context.AmountToRefund;

        return result;
    }

    protected virtual CyberSourceVoidPaymentRequest PrepareVoidPaymentRequest(PaymentIn payment)
    {
        var result = AbstractTypeFactory<CyberSourceVoidPaymentRequest>.TryCreateInstance();

        result.OuterPaymentId = payment.OuterId;
        result.Sandbox = Sandbox;
        result.Payment = payment;

        return result;
    }

    #endregion

    #region Post Process Results

    private PostProcessPaymentRequestResult GetPostPaymentResult(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    ) => response.Status switch
    {
        // PtsV2PaymentsPost201Response described here
        // https://github.com/CyberSource/cybersource-rest-client-node/blob/master/docs/PtsV2PaymentsPost201Response.md
        CyberSourceRequest.PaymentStatus.Authorized => PaymentApproved(response, payment, order),
        CyberSourceRequest.PaymentStatus.Declined
            or CyberSourceRequest.PaymentStatus.AuthorizedRiskDeclined
            => PaymentDeclined(response, payment),

        CyberSourceRequest.PaymentStatus.InvalidRequest => PaymentInvalid(response, payment),

        // note: see the CyberSourceClient.RefreshPaymentStatus method too
        CyberSourceRequest.PaymentStatus.PendingAuthentication
            or CyberSourceRequest.PaymentStatus.PartialAuthorized
            or CyberSourceRequest.PaymentStatus.AuthorizedPendingReview
            or CyberSourceRequest.PaymentStatus.PendingReview
            or CyberSourceRequest.PaymentStatus.Pending
            or CyberSourceRequest.PaymentStatus.Transmitted => PaymentPending(response, payment),

        _ => new PostProcessPaymentRequestResult
        {
            ErrorMessage = response.ErrorInformation?.Message
                           ?? response.ErrorInformation?.Reason,
            IsSuccess = false,
        },
    };


    private PostProcessPaymentRequestResult PaymentApproved(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    )
    {
        var result = new PostProcessPaymentRequestResult
        {
            NewPaymentStatus = SingleMessageMode ? PaymentStatus.Paid : PaymentStatus.Authorized,
            OrderId = order.Id,
            OuterId = response.Id,
            IsSuccess = true,
        };

        payment.PaymentStatus = result.NewPaymentStatus;
        payment.Status = result.NewPaymentStatus.ToString();
        payment.IsApproved = true;
        payment.OuterId = result.OuterId;
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
        payment.OuterId = response.Id;
        payment.Status = PaymentStatus.Pending.ToString();

        return new PostProcessPaymentRequestResult
        {
            NewPaymentStatus = PaymentStatus.Pending,
            ErrorMessage = errorMessage,
            IsSuccess = true,
        };
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
