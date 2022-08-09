using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Worldline
{
    /// <summary>
    /// Represents settings of the PayPal Standard payment plugin
    /// </summary>
    public class WorldlinePaymentSettings : ISettings
    { 
       /// <summary>
      /// Gets or sets a utf8 
      /// </summary>
        public string utf8 { get; set; }
        /// <summary>
        /// Gets or sets a authenticity token
        /// </summary>
        public string authenticity_token { get; set; }
        /// <summary>
        /// Gets or sets a merchantCode
        /// </summary>
        public string merchantCode { get; set; }

        /// <summary>
        /// Gets or sets a MerchantSchemeCode
        /// </summary>
        public string merchantSchemeCode { get; set; }

        /// <summary>
        /// Gets or sets a SALT
        /// </summary>
        public string SALT { get; set; }

        /// <summary>
        /// Gets or sets a Currency
        /// </summary>
        public string currency { get; set; }

        /// <summary>
        /// Gets or sets a TypeOfPayment
        /// </summary>
        public string typeOfPayment { get; set; }

        /// <summary>
        /// Gets or sets a Primary Color
        /// </summary>
        public string primaryColor { get; set; }

        /// <summary>
        /// Gets or sets a secondary Color
        /// </summary>
        public string secondaryColor { get; set; }

        /// <summary>
        /// Gets or sets a Button Color1
        /// </summary>
        public string buttonColor1 { get; set; }

        /// <summary>
        /// Gets or sets a Button Color2
        /// </summary>
        public string buttonColor2 { get; set; }

        /// <summary>
        /// Gets or sets a logo URL 
        /// </summary>
        public string logoURL { get; set; }

        /// <summary>
        /// Enables or disables Express pay
        /// </summary>
        public string enableExpressPay { get; set; }

        /// <summary>
        /// Enables or disables separate Card Mode
        /// </summary>
        public string separateCardMode { get; set; }

        /// <summary>
        /// If this feature is enabled, then bank page will open in new window
        /// </summary>
        public string enableNewWindowFlow { get; set; }

        /// <summary>
        /// Gets or sets a merchant Message
        /// </summary>
        public string merchantMessage { get; set; }

        /// <summary>
        /// Gets or sets a disclaimer message
        /// </summary>
        public string disclaimerMessage { get; set; }

        /// <summary>
        /// Selects payment mode
        /// </summary>
        public string paymentMode { get; set; }


        /// <summary>
        /// Gets or sets a Payment Mode Order 
        /// </summary>
        public string paymentModeOrder { get; set; }


        ///// <summary>
        ///// Sets merchantLogoUrl
        ///// </summary>
        //public string merchantLogoUrl { get; set; }


        /// <summary>
        /// Gets or sets a merchant Msg
        /// </summary>
        public string merchantMsg { get; set; }
        /// <summary>
        /// Gets or sets a disclaimer Msg
        /// </summary>
        public string disclaimerMsg { get; set; }

        /// <summary>
        /// Gets or sets a showPGResponseMsg 
        /// </summary>
        public string showPGResponseMsg { get; set; }
        /// <summary>
        /// Gets or sets a denableAbortResponse
        /// </summary>
        public string enableAbortResponse { get; set; }
        
        /// <summary>
        /// Enables or disables Instrument DeRegistration
        /// </summary>
        public string enableInstrumentDeRegistration { get; set; }


        /// <summary>
        /// Enables or disables Transaction Type
        /// </summary>
        public string transactionType { get; set; }

        /// <summary>
        /// Show or hide Saved Instruments
        /// </summary>
        public string hideSavedInstruments { get; set; }


        /// <summary>
        /// Enables or disables saveInstrument
        /// </summary>
        public string saveInstrument { get; set; }

        /// <summary>
        /// Enables or disablesTransaction Message On Popup
        /// </summary>
        public string displayTransactionMessageOnPopup { get; set; }


        /// <summary>
        /// Enables or disables payment gateway on page
        /// </summary>
        public string embedPaymentGatewayOnPage { get; set; }


        /// <summary>
        /// Enables or disables eMandate
        /// </summary>
        public string enableSI { get; set; }


        /// <summary>
        /// Enables or disables hide SI details from the customer
        /// </summary>
        public string hideSIDetails { get; set; }

        /// <summary>
        /// Enables or disables hide the confirmation screen 
        /// </summary>
        public string hideSIConfirmation { get; set; }

        /// <summary>
        /// Enables or disables expanded mode 
        /// </summary>
        public string expandSIDetails { get; set; }


        /// <summary>
        /// Enables or disables expanded mode 
        /// </summary>
        public string enableDebitDay { get; set; }

        /// <summary>
        /// Enables or disables show SI Response Msg
        /// </summary>
        public string showSIResponseMsg { get; set; }
        /// <summary>
        /// Enables or disables show SI Confirmation
        /// </summary>
        public string showSIConfirmation { get; set; }
        /// <summary>
        /// Enables or disables txn for nonsi cards
        /// </summary>
        public string enableTxnForNonSICards { get; set; }

        /// <summary>
        /// Enables or disables all modes with si
        /// </summary>
        public string showAllModesWithSI { get; set; }
        /// <summary>
        /// Enables or disables SI details at Merchant end
        /// </summary>
        public string siDetailsAtMerchantEnd { get; set; }

        /// <summary>
        /// Gets or sets amounttype
        /// </summary>
        public string amounttype { get; set; }
        /// <summary>
        /// Gets or sets Frequency
        /// </summary>
        public string frequency { get; set; }


        /// <summary>
        /// Gets or sets a value indicating whether to use sandbox (testing environment)
        /// </summary>
        public bool UseSandbox { get; set; }

        /// <summary>
        /// Gets or sets a business email
        /// </summary>
        public string BusinessEmail { get; set; }

        /// <summary>
        /// Gets or sets PDT identity token
        /// </summary>
        public string PdtToken { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to pass info about purchased items to PayPal
        /// </summary>
        public bool PassProductNamesAndTotals { get; set; }

        /// <summary>
        /// Gets or sets an additional fee
        /// </summary>
        public decimal AdditionalFee { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to "additional fee" is specified as percentage. true - percentage, false - fixed value.
        /// </summary>
        public bool AdditionalFeePercentage { get; set; }

        /// <summary>
        /// Selects payment mode
        /// </summary>
        public bool SeperateCardMode { get; set; }
    }
}
