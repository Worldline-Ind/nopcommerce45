using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Worldline.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.utf8")]
        public string Utf8 { get; set; }
        public bool Utf8_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.authenticity_token")]
        public string Authenticity_token { get; set; }
        public bool Authenticity_token_OverrideForStore { get; set; }
        [NopResourceDisplayName("Merchant Code")]
        public string MerchantCode { get; set; }
        public bool MerchantCode_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.MerchantSchemeCode")]
        public string MerchantSchemeCode { get; set; }
        public bool MerchantSchemeCode_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.SALT")]
        public string SALT { get; set; }
        public bool SALT_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.Currency")]
        public string Currency { get; set; }
        public bool Currency_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.TypeOfPayment")]
        public string TypeOfPayment { get; set; }
        public bool TypeOfPayment_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.PrimaryColor")]
        public string PrimaryColor { get; set; }
        public bool PrimaryColor_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.SecondaryColor")]
        public string SecondaryColor { get; set; }
        public bool SecondaryColor_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.ButtonColor1")]
        public string ButtonColor1 { get; set; }
        public bool ButtonColor1_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.buttonColor2")]
        public string ButtonColor2 { get; set; }
        public bool ButtonColor2_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.LogoURL")]
        public string LogoURL { get; set; }
        public bool LogoURL_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableExpressPay")]
        public string EnableExpressPay { get; set; }
        public bool EnableExpressPay_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.separateCardMode")]
        public string SeparateCardMode { get; set; }
        public bool SeparateCardMode_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableNewWindowFlow")]
        public string EnableNewWindowFlow { get; set; }
        public bool EnableNewWindowFlow_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.merchantMessage")]
        public string MerchantMessage { get; set; }
        public bool MerchantMessage_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.disclaimerMessage")]
        public string DisclaimerMessage { get; set; }
        public bool DisclaimerMessage_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.paymentMode")]
        public string PaymentMode { get; set; }
        public bool PaymentMode_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.paymentModeOrder")]
        public string PaymentModeOrder { get; set; }
        public bool PaymentModeOrder_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableInstrumentDeRegistration")]
        public string EnableInstrumentDeRegistration { get; set; }
        public bool EnableInstrumentDeRegistration_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.transactionType")]
        public string TransactionType { get; set; }
        public bool TransactionType_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.hideSavedInstruments")]
        public string HideSavedInstruments { get; set; }
        public bool HideSavedInstruments_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.saveInstrument")]
        public string SaveInstrument { get; set; }
        public bool SaveInstrument_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.displayTransactionMessageOnPopup")]
        public string DisplayTransactionMessageOnPopup { get; set; }
        public bool DisplayTransactionMessageOnPopup_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.embedPaymentGatewayOnPage")]
        public string EmbedPaymentGatewayOnPage { get; set; }
        public bool EmbedPaymentGatewayOnPage_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableSI")]
        public string EnableSI { get; set; }
        public bool EnableSI_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.hideSIDetails")]
        public string HideSIDetails { get; set; }
        public bool HideSIDetails_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.hideSIConfirmation")]
        public string HideSIConfirmation { get; set; }
        public bool HideSIConfirmation_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.expandSIDetails")]
        public string ExpandSIDetails { get; set; }
        public bool ExpandSIDetails_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableDebitDay")]
        public string EnableDebitDay { get; set; }
        public bool EnableDebitDay_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showSIResponseMsg")]
        public string ShowSIResponseMsg { get; set; }
        public bool ShowSIResponseMsg_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showSIConfirmation")]
        public string ShowSIConfirmation { get; set; }
        public bool ShowSIConfirmation_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableTxnForNonSICards")]
        public string EnableTxnForNonSICards { get; set; }
        public bool EnableTxnForNonSICards_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showAllModesWithSI")]
        public string ShowAllModesWithSI { get; set; }
        public bool ShowAllModesWithSI_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.siDetailsAtMerchantEnd")]
        public string SiDetailsAtMerchantEnd { get; set; }
        public bool SiDetailsAtMerchantEnd_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.amounttype")]
        public string AmountType { get; set; }
        public bool AmountType_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.frequency")]
        public string Frequency { get; set; }
        public bool Frequency_OverrideForStore { get; set; }



        //[NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableTxnForNonSICards")]
        //public string merchantLogoUrl { get; set; }
        //public bool merchantLogoUrl_OverrideForStore { get; set; }
        //[NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showAllModesWithSI")]
        //public string merchantMsg { get; set; }
        //public bool merchantMsg_OverrideForStore { get; set; }
        //[NopResourceDisplayName("Plugins.Payments.Worldline.Fields.siDetailsAtMerchantEnd")]
        //public string disclaimerMsg { get; set; }
        //public bool disclaimerMsg_OverrideForStore { get; set; }
        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.showPGResponseMsg")]
        public string ShowPGResponseMsg { get; set; }
        public bool ShowPGResponseMsg_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.enableAbortResponse")]
        public string EnableAbortResponse { get; set; }
        public bool EnableAbortResponse_OverrideForStore { get; set; }


        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.UseSandbox")]
        public bool UseSandbox { get; set; }
        public bool UseSandbox_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.BusinessEmail")]
        public string BusinessEmail { get; set; }
        public bool BusinessEmail_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.PDTToken")]
        public string PdtToken { get; set; }
        public bool PdtToken_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals")]
        public bool PassProductNamesAndTotals { get; set; }
        public bool PassProductNamesAndTotals_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFee_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Worldline.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }
        public bool AdditionalFeePercentage_OverrideForStore { get; set; }
    }
}