namespace VirtoCommerce.CyberSourcePayment.Core.Models;

public class CyberSourceRequest
{
    public static class PaymentStatus
    {
        public const string Authorized = "AUTHORIZED";
        public const string Declined = "DECLINED";
        public const string PartialAuthorized = "PARTIAL_AUTHORIZED";
        public const string AuthorizedPendingReview = "AUTHORIZED_PENDING_REVIEW";
        public const string AuthorizedRiskDeclined = "AUTHORIZED_RISK_DECLINED";
        public const string InvalidRequest = "INVALID_REQUEST";
        public const string PendingAuthentication = "PENDING_AUTHENTICATION";
        public const string PendingReview = "PENDING_REVIEW";
        public const string Pending = "PENDING";
        public const string Transmitted = "TRANSMITTED";
        public const string Voided = "VOIDED";
        public const string Cancelled = "CANCELLED";
        public const string Failed = "FAILED";
    }

    public bool Sandbox { get; set; }
    public bool SingleMessageMode { get; set; }
    public string OuterPaymentId { get; set; }
}
