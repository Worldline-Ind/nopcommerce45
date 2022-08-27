using System;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Worldline.Models
{
    public record PaymentViewModel : BaseNopModel
    {
        public string MerchantCode { get; set; }
        public string Amount { get; set; }
        public string Currency { get; set; }
        public string PrimaryColor { get; set; }
        public string SecondaryColor { get; set; }
        public string ButtonColor1 { get; set; }
        public string ButtonColor2 { get; set; }
        public string MerchantSchemeCode { get; set; }
        public string SALT { get; set; }
        public string PaymentMode { get; set; }
        public string PaymentModeOrder { get; set; }
        public string PaymentMerchantLogoUrl { get; set; }
        public string MerchantMsg { get; set; }
        public string DisclaimerMsg { get; set; }
        public bool ShowPGResponseMsg { get; set; }
        public bool EnableAbortResponse { get; set; }
        public bool EnableExpressPay { get; set; }
        public bool EnableNewWindowFlow { get; set; }
        public bool EnableDebitDay { get; set; }
        public bool SiDetailsAtMerchantEnd { get; set; }
        public bool EnableSI { get; set; }
        public bool EmbedPaymentGatewayOnPage { get; set; }
        public bool SeparateCardMode { get; set; }
        public string Token { get; set; }
        public int CustomerId { get; set; }
        public string DebitStartDate{ get; set; }
        public string DebitEndDate { get; set; }
        public string ReturnUrl { get; set; }
        public string TransactionId { get; set; }
        
    }
}
