namespace Ordering.Api.Validators
{
    public static class PaymentMethodIdRules
    {
        public const string Pattern = "^pm_[A-Za-z0-9_]+$";
        public const string Message = "Payment method id must be a Stripe payment method id (pm_...).";
    }
}
