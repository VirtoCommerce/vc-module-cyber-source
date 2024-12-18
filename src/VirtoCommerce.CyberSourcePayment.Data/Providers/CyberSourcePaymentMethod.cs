using System;
using System.Collections.Specialized;
using System.Linq;
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
    ICyberSourceClient cyberSourceClient
) : PaymentMethod(nameof(CyberSourcePaymentMethod))
{
    private bool Sandbox => Settings.GetValue<bool>(ModuleConstants.Settings.General.CyberSourceSandbox);

    public override PaymentMethodGroupType PaymentMethodGroupType => PaymentMethodGroupType.BankCard;
    public override PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;

    public override ProcessPaymentRequestResult ProcessPayment(ProcessPaymentRequest request)
    {
        var store = (Store)request.Store;

        if (store == null || (store.SecureUrl.IsNullOrEmpty() && store.Url.IsNullOrEmpty()))
        {
            // todo: publish error, url required
            throw new Exception();
        }

        var url = store.SecureUrl.IsNullOrEmpty() ? store.Url : store.SecureUrl;
        var cardTypesValue = Settings.GetValue<string>(ModuleConstants.Settings.General.CyberSourceCardTypes);
        var cardTypes = cardTypesValue.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.IsNullOrEmpty())
            .Select(x => x.Trim().ToUpperInvariant())
            .ToArray();

        var jwt = cyberSourceClient.GenerateCaptureContext(Sandbox, url, cardTypes);

        var result = new ProcessPaymentRequestResult
        {
            IsSuccess = true,
            NewPaymentStatus = PaymentStatus.Pending,
            PublicParameters = new()
            {
                {"jwt", jwt.KeyId },
                // todo: remove it before release
                {"clientScript", "https://testflex.cybersource.com/microform/bundle/v2.0.2/flex-microform.min.js"},
            }
        };

        var payment = (PaymentIn)request.Payment;
        payment.PaymentStatus = PaymentStatus.Pending;
        payment.Status = payment.PaymentStatus.ToString();

        return result;
    }

    public override PostProcessPaymentRequestResult PostProcessPayment(PostProcessPaymentRequest request)
    {
        var token = request.Parameters.Get("token"); // todo: should I validate token here?
        var payment = (PaymentIn)request.Payment;
        var order = (CustomerOrder)request.Order;

        var paymentResult = cyberSourceClient.ProcessPayment(Sandbox, token, payment, order);

        var result = GetPostPaymentResult(paymentResult, payment, order);
        return result;
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


    #region Post Process Results

    private PostProcessPaymentRequestResult GetPostPaymentResult(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    ) => response.Status switch
    {
        // PtsV2PaymentsPost201Response described here
        // https://github.com/CyberSource/cybersource-rest-client-node/blob/master/docs/PtsV2PaymentsPost201Response.md
        "AUTHORIZED" => PaymentApproved(response, payment, order),
        "DECLINED" => PaymentDeclined(response, payment, order),

        "PARTIAL_AUTHORIZED"
            or "AUTHORIZED_PENDING_REVIEW"
            or "AUTHORIZED_RISK_DECLINED"
            or "INVALID_REQUEST" => PaymentInvalid(response, payment, order),

        "PENDING_AUTHENTICATION"
            or "PENDING_REVIEW" => PaymentPending(response, payment, order),
        _ => new PostProcessPaymentRequestResult
        {
            ErrorMessage = response.ErrorInformation?.Message
                           ?? response.ErrorInformation?.Reason,
            IsSuccess = false
        }
    };


    private PostProcessPaymentRequestResult PaymentApproved(
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
            ResponseData = JsonConvert.SerializeObject(response)
        };

        payment.Transactions.Add(transaction);

        return result;
    }

    private PostProcessPaymentRequestResult PaymentDeclined(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    )
    {
        var transactionMessage = response.ErrorInformation.Message;

        payment.Status = PaymentStatus.Declined.ToString();
        payment.ProcessPaymentResult = new ProcessPaymentRequestResult
        {
            ErrorMessage = $"Your transaction was declined: {transactionMessage}",
        };
        payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

        return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };

    }

    private PostProcessPaymentRequestResult PaymentPending(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    )
    {
        var transactionMessage = response.ErrorInformation.Message;

        payment.ProcessPaymentResult = new ProcessPaymentRequestResult
        {
            ErrorMessage = $"Your transaction was held for review: {transactionMessage}",
        };
        payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

        return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };

    }

    private PostProcessPaymentRequestResult PaymentInvalid(
        PtsV2PaymentsPost201Response response,
        PaymentIn payment,
        CustomerOrder order
    )
    {
        var transactionMessage = response.ErrorInformation.Message;

        payment.Status = PaymentStatus.Error.ToString();
        payment.ProcessPaymentResult = new ProcessPaymentRequestResult
        {
            ErrorMessage = $"There was an error processing your transaction: {transactionMessage}",
        };
        payment.Comment = $"{payment.ProcessPaymentResult.ErrorMessage}{Environment.NewLine}";

        return new PostProcessPaymentRequestResult { ErrorMessage = payment.ProcessPaymentResult.ErrorMessage };

    }

    #endregion
}
