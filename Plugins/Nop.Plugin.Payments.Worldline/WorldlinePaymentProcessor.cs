using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Plugin.Payments.Worldline.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Security;
using Nop.Services.Tax;
using Nop.Web.Framework;
using Nop.Web.Framework.Menu;

namespace Nop.Plugin.Payments.Worldline
{
    /// <summary>
    /// Worldline payment processor
    /// </summary>
    public class WorldlinePaymentProcessor : BasePlugin, IPaymentMethod, IAdminMenuPlugin
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly IAddressService _addressService;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly ICountryService _countryService;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IOrderService _orderService;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IProductService _productService;
        private readonly ISettingService _settingService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ITaxService _taxService;
        private readonly IWebHelper _webHelper;
        private readonly WorldlineStandardHttpClient _worldlineHttpClient;
        private readonly WorldlinePaymentSettings _worldlinePaymentSettings;
        private readonly IPermissionService _permissionService;
        //private readonly WorldlineHelper _worldlineHelper;

        #endregion

        #region Ctor

        public WorldlinePaymentProcessor(CurrencySettings currencySettings,
            IAddressService addressService,
            ICheckoutAttributeParser checkoutAttributeParser,
            ICountryService countryService,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IOrderService orderService,
            IOrderTotalCalculationService orderTotalCalculationService,
            IProductService productService,
            ISettingService settingService,
            IStateProvinceService stateProvinceService,
            ITaxService taxService,
            IWebHelper webHelper,
            WorldlineStandardHttpClient worldlineHttpClient,
            WorldlinePaymentSettings worldlinePaymentSettings)
        {
            _currencySettings = currencySettings;
            _addressService = addressService;
            _checkoutAttributeParser = checkoutAttributeParser;
            _countryService = countryService;
            _currencyService = currencyService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _orderService = orderService;
            _orderTotalCalculationService = orderTotalCalculationService;
            _productService = productService;
            _settingService = settingService;
            _stateProvinceService = stateProvinceService;
            _taxService = taxService;
            _webHelper = webHelper;
            _worldlineHttpClient = worldlineHttpClient;
            _worldlinePaymentSettings = worldlinePaymentSettings;
            //_worldlineHelper = worldlineHelper;
        }

        #endregion

        #region Utilities
        public Task ManageSiteMapAsync(SiteMapNode rootNode)
        {
            var worldlinePluginNode = new SiteMapNode()
            {
                SystemName = "Worldline",
                Title = "Worldline",
                IconClass = "far fa-dot-circle",
                Visible = true,
                RouteValues = new RouteValueDictionary() { { "area", AreaNames.Admin } },
            };
            var menuItemOfflineVerificaion = new SiteMapNode()
            {
                SystemName = "Worldline Offline Verification",
                Title = "Offline Verification",
                ControllerName = "Worldline",
                ActionName = "OfflineVerification",
                IconClass = "far fa-dot-circle",
                Visible = true,
                RouteValues = new RouteValueDictionary() { { "area", AreaNames.Admin } },
            };
            var menuItemReconcile = new SiteMapNode()
            {
                SystemName = "Worldline Reconcile",
                Title = "Reconcile",
                ControllerName = "Worldline",
                ActionName = "Reconcile",
                IconClass = "far fa-dot-circle",
                Visible = true,
                RouteValues = new RouteValueDictionary() { { "area", AreaNames.Admin } },
            };
            worldlinePluginNode.ChildNodes.Add(menuItemOfflineVerificaion);
            worldlinePluginNode.ChildNodes.Add(menuItemReconcile);
            var pluginNode = rootNode.ChildNodes.FirstOrDefault(x => x.SystemName == "Third party plugins");
            if (pluginNode != null)
            {
                pluginNode.ChildNodes.Add(worldlinePluginNode);
            }
            else
            {
                rootNode.ChildNodes.Add(worldlinePluginNode);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Create common query parameters for the request
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the created query parameters
        /// </returns>
        private async Task<IDictionary<string, string>> CreateQueryParametersAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //get store location
            var storeLocation = _webHelper.GetStoreLocation();

            //choosing correct order address
            var orderAddress = await _addressService.GetAddressByIdAsync(
                (postProcessPaymentRequest.Order.PickupInStore ? postProcessPaymentRequest.Order.PickupAddressId : postProcessPaymentRequest.Order.ShippingAddressId) ?? 0);

            //create query parameters
            return new Dictionary<string, string>
            {
                //Worldline ID or an email address associated with your Worldline account
                ["business"] = _worldlinePaymentSettings.BusinessEmail,

                //the character set and character encoding
                ["charset"] = "utf-8",

                //set return method to "2" (the customer redirected to the return URL by using the POST method, and all payment variables are included)
                ["rm"] = "2",

                ["bn"] = WorldlineHelper.NopCommercePartnerCode,
                ["currency_code"] = (await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId))?.CurrencyCode,

                //order identifier
                ["invoice"] = postProcessPaymentRequest.Order.CustomOrderNumber,
                ["custom"] = postProcessPaymentRequest.Order.OrderGuid.ToString(),

                //PDT, IPN and cancel URL
                ["return"] = $"{storeLocation}Plugins/Worldline/PDTHandler",
                ["notify_url"] = $"{storeLocation}Plugins/Worldline/IPNHandler",
                ["cancel_return"] = $"{storeLocation}Plugins/Worldline/CancelOrder",

                //shipping address, if exists
                ["no_shipping"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "1" : "2",
                ["address_override"] = postProcessPaymentRequest.Order.ShippingStatus == ShippingStatus.ShippingNotRequired ? "0" : "1",
                ["first_name"] = orderAddress?.FirstName,
                ["last_name"] = orderAddress?.LastName,
                ["address1"] = orderAddress?.Address1,
                ["address2"] = orderAddress?.Address2,
                ["city"] = orderAddress?.City,
                ["state"] = (await _stateProvinceService.GetStateProvinceByAddressAsync(orderAddress))?.Abbreviation,
                ["country"] = (await _countryService.GetCountryByAddressAsync(orderAddress))?.TwoLetterIsoCode,
                ["zip"] = orderAddress?.ZipPostalCode,
                ["email"] = orderAddress?.Email
            };
        }

        /// <summary>
        /// Add order items to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task AddItemsParametersAsync(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //upload order items
            parameters.Add("cmd", "_cart");
            parameters.Add("upload", "1");

            var cartTotal = decimal.Zero;
            var roundedCartTotal = decimal.Zero;
            var itemCount = 1;

            //add shopping cart items
            foreach (var item in await _orderService.GetOrderItemsAsync(postProcessPaymentRequest.Order.Id))
            {
                var roundedItemPrice = Math.Round(item.UnitPriceExclTax, 2);

                var product = await _productService.GetProductByIdAsync(item.ProductId);

                //add query parameters
                parameters.Add($"item_name_{itemCount}", product.Name);
                parameters.Add($"amount_{itemCount}", roundedItemPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", item.Quantity.ToString());

                cartTotal += item.PriceExclTax;
                roundedCartTotal += roundedItemPrice * item.Quantity;
                itemCount++;
            }

            //add checkout attributes as order items
            var checkoutAttributeValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(postProcessPaymentRequest.Order.CheckoutAttributesXml);
            var customer = await _customerService.GetCustomerByIdAsync(postProcessPaymentRequest.Order.CustomerId);

            await foreach (var (attribute, values) in checkoutAttributeValues)
            {
                await foreach (var attributeValue in values)
                {
                    var (attributePrice, _) = await _taxService.GetCheckoutAttributePriceAsync(attribute, attributeValue, false, customer);
                    var roundedAttributePrice = Math.Round(attributePrice, 2);

                    //add query parameters
                    if (attribute == null)
                        continue;

                    parameters.Add($"item_name_{itemCount}", attribute.Name);
                    parameters.Add($"amount_{itemCount}", roundedAttributePrice.ToString("0.00", CultureInfo.InvariantCulture));
                    parameters.Add($"quantity_{itemCount}", "1");

                    cartTotal += attributePrice;
                    roundedCartTotal += roundedAttributePrice;
                    itemCount++;
                }
            }

            //add shipping fee as a separate order item, if it has price
            var roundedShippingPrice = Math.Round(postProcessPaymentRequest.Order.OrderShippingExclTax, 2);
            if (roundedShippingPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Shipping fee");
                parameters.Add($"amount_{itemCount}", roundedShippingPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderShippingExclTax;
                roundedCartTotal += roundedShippingPrice;
                itemCount++;
            }

            //add payment method additional fee as a separate order item, if it has price
            var roundedPaymentMethodPrice = Math.Round(postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax, 2);
            if (roundedPaymentMethodPrice > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Payment method fee");
                parameters.Add($"amount_{itemCount}", roundedPaymentMethodPrice.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.PaymentMethodAdditionalFeeExclTax;
                roundedCartTotal += roundedPaymentMethodPrice;
                itemCount++;
            }

            //add tax as a separate order item, if it has positive amount
            var roundedTaxAmount = Math.Round(postProcessPaymentRequest.Order.OrderTax, 2);
            if (roundedTaxAmount > decimal.Zero)
            {
                parameters.Add($"item_name_{itemCount}", "Tax amount");
                parameters.Add($"amount_{itemCount}", roundedTaxAmount.ToString("0.00", CultureInfo.InvariantCulture));
                parameters.Add($"quantity_{itemCount}", "1");

                cartTotal += postProcessPaymentRequest.Order.OrderTax;
                roundedCartTotal += roundedTaxAmount;
            }

            if (cartTotal > postProcessPaymentRequest.Order.OrderTotal)
            {
                //get the difference between what the order total is and what it should be and use that as the "discount"
                var discountTotal = Math.Round(cartTotal - postProcessPaymentRequest.Order.OrderTotal, 2);
                roundedCartTotal -= discountTotal;

                //gift card or rewarded point amount applied to cart in nopCommerce - shows in Worldline as "discount"
                parameters.Add("discount_amount_cart", discountTotal.ToString("0.00", CultureInfo.InvariantCulture));
            }

            //save order total that actually sent to Worldline (used for PDT order total validation)
            await _genericAttributeService.SaveAttributeAsync(postProcessPaymentRequest.Order, WorldlineHelper.OrderTotalSentToWorldline, roundedCartTotal);
        }

        /// <summary>
        /// Add order total to the request query parameters
        /// </summary>
        /// <param name="parameters">Query parameters</param>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        private async Task AddOrderTotalParametersAsync(IDictionary<string, string> parameters, PostProcessPaymentRequest postProcessPaymentRequest)
        {
            //round order total
            var roundedOrderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);

            parameters.Add("cmd", "_xclick");
            parameters.Add("item_name", $"Order Number {postProcessPaymentRequest.Order.CustomOrderNumber}");
            parameters.Add("amount", roundedOrderTotal.ToString("0.00", CultureInfo.InvariantCulture));

            //save order total that actually sent to Worldline (used for PDT order total validation)
            await _genericAttributeService.SaveAttributeAsync(postProcessPaymentRequest.Order, WorldlineHelper.OrderTotalSentToWorldline, roundedOrderTotal);
        }

        #endregion

        #region Methods
        /// <summary>
        /// Gets PDT details
        /// </summary>
        /// <param name="tx">TX</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result, Values, Response
        /// </returns>
        public async Task<(bool result, Dictionary<string, string> values, string response)> GetPdtDetailsAsync(string tx)
        {
            var response = WebUtility.UrlDecode(await _worldlineHttpClient.GetPdtDetailsAsync(tx));

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool firstLine = true, success = false;
            foreach (var l in response.Split('\n'))
            {
                var line = l.Trim();
                if (firstLine)
                {
                    success = line.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase);
                    firstLine = false;
                }
                else
                {
                    var equalPox = line.IndexOf('=');
                    if (equalPox >= 0)
                        values.Add(line[0..equalPox], line[(equalPox + 1)..]);
                }
            }

            return (success, values, response);
        }

        /// <summary>
        /// Verifies IPN
        /// </summary>
        /// <param name="formString">Form string</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result, Values
        /// </returns>
        public async Task<(bool result, Dictionary<string, string> values)> VerifyIpnAsync(string formString)
        {
            var response = WebUtility.UrlDecode(await _worldlineHttpClient.VerifyIpnAsync(formString));
            var success = response.Trim().Equals("VERIFIED", StringComparison.OrdinalIgnoreCase);

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in formString.Split('&'))
            {
                var line = l.Trim();
                var equalPox = line.IndexOf('=');
                if (equalPox >= 0)
                    values.Add(line[0..equalPox], line[(equalPox + 1)..]);
            }

            return (success, values);
        }

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult());
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var queryParameters = await CreateQueryParametersAsync(postProcessPaymentRequest);
            var parameters = new Dictionary<string, string>(queryParameters);
            string amount, orderid, txnId;
            //txnId = GenerateRandomString();
            amount = postProcessPaymentRequest.Order.OrderTotal.ToString("0.00");
            orderid = postProcessPaymentRequest.Order.Id.ToString();
            txnId = GenerateRandomString(15) + orderid;
            List<string> hashValues = new List<string>();
            hashValues.Add(_worldlinePaymentSettings.merchantCode);
            hashValues.Add(txnId);
            hashValues.Add(amount);
            hashValues.Add(string.Empty);
            hashValues.Add(string.Empty);
            hashValues.Add(string.Empty);
            hashValues.Add(string.Empty);
            hashValues.Add(DateTime.Now.ToShortDateString());
            hashValues.Add(DateTime.Now.AddYears(10).ToShortDateString());
            hashValues.Add(amount);
            hashValues.Add("M");
            hashValues.Add(_worldlinePaymentSettings.frequency);
            hashValues.Add(string.Empty);
            hashValues.Add(string.Empty);
            hashValues.Add(string.Empty);
            hashValues.Add(string.Empty);
            hashValues.Add(_worldlinePaymentSettings.SALT);
            string input = string.Join('|', hashValues.ToArray());
            string token = GenerateSHA512String(input);
            
            await AddItemsParametersAsync(parameters, postProcessPaymentRequest);

            //remove null values from parameters
            parameters = parameters.Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value);
            _httpContextAccessor.HttpContext.Response.Cookies.Append("txnId", txnId);
            _httpContextAccessor.HttpContext.Response.Cookies.Append("orderid", orderid);
            _httpContextAccessor.HttpContext.Response.Cookies.Append("amount", amount);
            _httpContextAccessor.HttpContext.Response.Cookies.Append("token", token);
            var absoluteUri = $"{_webHelper.GetStoreLocation()}Plugins/Worldline/PaymentCheckout";
            _httpContextAccessor.HttpContext.Response.Redirect(absoluteUri);
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the rue - hide; false - display.
        /// </returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the additional handling fee
        /// </returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
                _worldlinePaymentSettings.AdditionalFee, _worldlinePaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the capture payment result
        /// </returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            string amount, orderid, txnId, txnDate;
            var merchantcode = await _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode");
            var currency = await _settingService.GetSettingAsync("worldlinepaymentsettings.currency");
            amount = refundPaymentRequest.Order.OrderTotal.ToString("0.00");
            orderid = refundPaymentRequest.Order.Id.ToString();
            txnId = refundPaymentRequest.Order.AuthorizationTransactionCode;
            txnDate = refundPaymentRequest.Order.CreatedOnUtc.Date.ToString("dd-MM-yyyy");
            var data = new
            {
                merchant = new { identifier = merchantcode.Value },
                cart = new
                {
                },
                transaction = new
                {
                    deviceIdentifier = "S",
                    amount = amount,
                    currency = currency.Value,
                    dateTime = txnDate,
                    token = txnId,
                    requestType = "R"
                }
            };
            HttpContent content = new StringContent(JsonConvert.SerializeObject(data));
            HttpClient client = new HttpClient();
            client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var response = await client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", content);
            var respStr = response.Content.ReadAsStringAsync();
            JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
            var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();
            List<JToken> tokens = jsonData.Children().ToList();
            string statusCode = tokens[6]["paymentTransaction"]["statusCode"].ToString();

            if (statusCode == "0400")
            {
                return await Task.FromResult(new RefundPaymentResult { NewPaymentStatus = Core.Domain.Payments.PaymentStatus.Refunded });
            }
            else
            {
                RefundPaymentResult refundPaymentResult = new RefundPaymentResult();
                refundPaymentResult.AddError(tokens[6]["paymentTransaction"]["errorMessage"].ToString());
                refundPaymentResult.AddError(tokens[6]["paymentTransaction"]["statusMessage"].ToString());
                return await Task.FromResult(refundPaymentResult);
            }
            
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }
        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of validating errors
        /// </returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the payment info holder
        /// </returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/Worldline/Configure";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "PaymentWorldline";
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task InstallAsync()
        {
            //settings
            await _settingService.SaveSettingAsync(new WorldlinePaymentSettings
            {
                UseSandbox = true
            });

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Worldline.Fields.AdditionalFee"] = "Additional fee",
                ["Plugins.Payments.Worldline.Fields.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
                ["Plugins.Payments.Worldline.Fields.AdditionalFeePercentage"] = "Additional fee. Use percentage",
                ["Plugins.Payments.Worldline.Fields.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payments.Worldline.Fields.BusinessEmail"] = "Business Email",
                ["Plugins.Payments.Worldline.Fields.BusinessEmail.Hint"] = "Specify your Worldline business email.",
                ["Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals"] = "Pass product names and order totals to Worldline",
                ["Plugins.Payments.Worldline.Fields.PassProductNamesAndTotals.Hint"] = "Check if product names and order totals should be passed to Worldline.",
                ["Plugins.Payments.Worldline.Fields.PDTToken"] = "PDT Identity Token",
                ["Plugins.Payments.Worldline.Fields.PDTToken.Hint"] = "Specify PDT identity token",
                ["Plugins.Payments.Worldline.Fields.RedirectionTip"] = "You will be redirected to Worldline site to complete the order.",
                ["Plugins.Payments.Worldline.Fields.UseSandbox"] = "Use Sandbox",
                ["Plugins.Payments.Worldline.Fields.UseSandbox.Hint"] = "Check to enable Sandbox (testing environment).",
                ["Plugins.Payments.Worldline.Fields.MerchantSchemeCode"] = "Merchant Scheme Code",
                ["Plugins.Payments.Worldline.Fields.TypeOfPayment.Hint"] = "For TEST mode amount will be charge 1",
                ["Plugins.Payments.Worldline.Fields.SALT"] = "SALT",
                ["Plugins.Payments.Worldline.Fields.Currency"] = "Currency",
                ["Plugins.Payments.Worldline.Fields.TypeOfPayment"] = "Type of Payment",
                ["Plugins.Payments.Worldline.Fields.PrimaryColor"] = "Primary Color",
                ["Plugins.Payments.Worldline.Fields.PrimaryColor.Hint"] = "Color value can be hex, rgb or actual color name",
                ["Plugins.Payments.Worldline.Fields.SecondaryColor"] = "Secondary Color",
                ["Plugins.Payments.Worldline.Fields.SecondaryColor.Hint"] = "Color value can be hex, rgb or actual color name",
                ["Plugins.Payments.Worldline.Fields.ButtonColor1"] = "Button Color 1",
                ["Plugins.Payments.Worldline.Fields.ButtonColor1.Hint"] = "Color value can be hex, rgb or actual color name",
                
                ["Plugins.Payments.Worldline.Fields.ButtonColor2"] = "Button Color 2",
                ["Plugins.Payments.Worldline.Fields.ButtonColor2.Hint"] = " Color value can be hex, rgb or actual color name",
               
                ["Plugins.Payments.Worldline.Fields.LogoURL"] = "Logo URL",
                ["Plugins.Payments.Worldline.Fields.LogoURL.Hint"] = "An absolute URL pointing to a logo image of merchant which will show on checkout popup",
                ["Plugins.Payments.Worldline.Fields.EnableExpressPay"] = "Enable Express Pay",
                ["Plugins.Payments.Worldline.Fields.EnableExpressPay.Hint"] = "To enable saved payments set its value to yes",
                ["Plugins.Payments.Worldline.Fields.SeparateCardMode"] = "Separate Card Mode",
                ["Plugins.Payments.Worldline.Fields.SeparateCardMode.Hint"] = "If this feature is enabled checkout shows two separate payment mode(Credit Card and Debit Card)",
                ["Plugins.Payments.Worldline.Fields.EnableNewWindowFlow"] = "Enable New Window Flow",
                ["Plugins.Payments.Worldline.Fields.EnableNewWindowFlow.Hint"] = "If this feature is enabled, then bank page will open in new window",
                ["Plugins.Payments.Worldline.Fields.MerchantMessage"] = "Merchant Message",
                ["Plugins.Payments.Worldline.Fields.MerchantMessage.Hint"] = "Customize message from merchant which will be shown to customer in checkout page",
                ["Plugins.Payments.Worldline.Fields.DisclaimerMessage"] = "Disclaimer Message",
                ["Plugins.Payments.Worldline.Fields.DisclaimerMessage.Hint"] = "Customize disclaimer message from merchant which will be shown to customer in checkout page",
                ["Plugins.Payments.Worldline.Fields.ShowPGResponseMsg"] = "Show PG Response Msg",
                ["Plugins.Payments.Worldline.Fields.EnableAbortResponse"] = "Enable Abort Response",
                ["Plugins.Payments.Worldline.Fields.PaymentMode"] = "Payment Mode",
                ["Plugins.Payments.Worldline.Fields.PaymentModeOrder"] = "Payment Mode Order",
                ["Plugins.Payments.Worldline.Fields.PaymentModeOrder.Hint"] = "Sequence in which he Payment Modes will be displayed on Worldline payments page.",
                ["Plugins.Payments.Worldline.Fields.Frequency"] = "Frequency",
                ["Plugins.Payments.Worldline.Fields.EnableInstrumentDeRegistration"] = "Enable Instrument De Registration",
                ["Plugins.Payments.Worldline.Fields.EnableInstrumentDeRegistration.Hint"] = "If this feature is enabled, you will have an option to delete saved cards",
            
                ["Plugins.Payments.Worldline.Fields.TransactionType"] = "Transaction Type",
                ["Plugins.Payments.Worldline.Fields.HideSavedInstruments"] = "Hide Saved Instruments",
                ["Plugins.Payments.Worldline.Fields.TransactionType"] = "Transaction Type",
                ["Plugins.Payments.Worldline.Fields.TransactionType.Hint"] = "If enabled checkout hides saved payment options even in case of enableExpressPay is enabled",
                ["Plugins.Payments.Worldline.Fields.SaveInstrument"] = "Save Instrument",
                ["Plugins.Payments.Worldline.Fields.SaveInstrument.Hint"] = "Enable this feature to vault instrument",
                ["Plugins.Payments.Worldline.Fields.DisplayTransactionMessageOnPopup"] = "Display Transaction Message OnPopup",
                ["Plugins.Payments.Worldline.Fields.EmbedPaymentGatewayOnPage"] = "Embed Payment Gateway OnPage",
                ["Plugins.Payments.Worldline.Fields.EnableSI"] = "Enable SI",
                ["Plugins.Payments.Worldline.Fields.EnableSI.Hint"] = "Enable eMandate using this feature",
                ["Plugins.Payments.Worldline.Fields.HideSIDetails"] = "Hide SI Details",
                ["Plugins.Payments.Worldline.Fields.HideSIDetails.Hint"] = "Enable this feature to hide SI details from the customer",
                ["Plugins.Payments.Worldline.Fields.HideSIConfirmation"] = "Hide SI Confirmation",
                ["Plugins.Payments.Worldline.Fields.HideSIConfirmation.Hint"] = "Enable this feature to hide the confirmation screen in eMandate/eNACH/eSign registration",
                ["Plugins.Payments.Worldline.Fields.ExpandSIDetails"] = "Expand SI Details",
                ["Plugins.Payments.Worldline.Fields.ExpandSIDetails.Hint"] = "Enable this feature to show eMandate/eNACH/eSign details in expanded mode by default",
                ["Plugins.Payments.Worldline.Fields.EnableDebitDay"] = "Enable Debit Day",
                ["Plugins.Payments.Worldline.Fields.EnableDebitDay.Hint"] = "Enable this feature to acccept debit day value eMandate/eNACH/eSign registration",
                ["Plugins.Payments.Worldline.Fields.ShowSIResponseMsg"] = "Show SI Response Msg",
                ["Plugins.Payments.Worldline.Fields.ShowSIResponseMsg.Hint"] = "Enable this feature to show eMandate/eNACH/eSign registrations details also in final checkout response",
                ["Plugins.Payments.Worldline.Fields.ShowSIConfirmation"] = "Show SI Confirmation",
                ["Plugins.Payments.Worldline.Fields.ShowSIConfirmation.Hint"] = "Enable this feature to show confirmation screen for registration",
                ["Plugins.Payments.Worldline.Fields.EnableTxnForNonSICards"] = "Enable Txn For Non SI Cards",
                ["Plugins.Payments.Worldline.Fields.EnableTxnForNonSICards.Hint"] = "Enable this feature to proceed with a normal transaction with same card details",
                ["Plugins.Payments.Worldline.Fields.ShowAllModesWithSI"] = "Show All Modes With SI",
                ["Plugins.Payments.Worldline.Fields.ShowAllModesWithSI.Hint"] = "Enable this feature to show all modes with SI",
                ["Plugins.Payments.Worldline.Fields.SiDetailsAtMerchantEnd"] = "Si Details At Merchant End",
                ["Plugins.Payments.Worldline.Fields.AmountType"] = "Amount Type",
                //["Plugins.Payments.Worldline.Fields.AmountType"] = "Amount Type",


                ["Plugins.Payments.Worldline.Instructions"] = @"
                    <p>
	                    <b>If you're using this gateway ensure that your primary store currency is supported by Worldline.</b>
	                    <br />
	                    <br />To use PDT, you must activate PDT and Auto Return in your Worldline account profile. You must also acquire a PDT identity token, which is used in all PDT communication you send to Worldline. Follow these steps to configure your account for PDT:<br />
	                    <br />1. Log in to your Worldline account (click <a href=""https://www.paypal.com/us/webapps/mpp/referral/paypal-business-account2?partner_id=9JJPJNNPQ7PZ8"" target=""_blank"">here</a> to create your account).
	                    <br />2. Click on the Profile button.
	                    <br />3. Click on the <b>Account Settings</b> link.
	                    <br />4. Select the <b>Website payments</b> item on left panel.
	                    <br />5. Find <b>Website Preferences</b> and click on the <b>Update</b> link.
	                    <br />6. Under <b>Auto Return</b> for <b>Website payments preferences</b>, select the <b>On</b> radio button.
	                    <br />7. For the <b>Return URL</b>, enter and save the URL on your site that will receive the transaction ID posted by Worldline after a customer payment (<em>{0}</em>).
                        <br />8. Under <b>Payment Data Transfer</b>, select the <b>On</b> radio button and get your <b>Identity token</b>.
	                    <br />9. Enter <b>Identity token</b> in the field below on the plugin configuration page.
                        <br />10. Click <b>Save</b> button on this page.
	                    <br />
                    </p>",
                ["Plugins.Payments.Worldline.PaymentMethodDescription"] = "You will be redirected to Worldline site to complete the payment",
                ["Plugins.Payments.Worldline.RoundingWarning"] = "It looks like you have \"ShoppingCartSettings.RoundPricesDuringCalculation\" setting disabled. Keep in mind that this can lead to a discrepancy of the order total amount, as Worldline only rounds to two decimals.",

            });

            await base.InstallAsync();
        }

        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<WorldlinePaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Worldline");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.Worldline.PaymentMethodDescription");
        }

        //public string GenerateRandomString()
        //{
        //    //var temp = Guid.NewGuid().ToString().Replace("-", string.Empty);
        //    //var random = temp.Substring(0, 15);
        //    //string shortDate = DateTime.Now.ToShortDateString().Replace("/", string.Empty).Replace("-", string.Empty);
        //    //return random.ToString() + shortDate;
        //    var temp = Guid.NewGuid().ToString().Replace("-", string.Empty);
        //    var barcode = Regex.Replace(temp, "[a-zA-Z]", string.Empty).Substring(0, 10);

        //    return barcode.ToString();
        //}
        public static string GenerateRandomString(int size)
        {
            Random random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            return new string(Enumerable.Repeat(chars, size)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
        public string GenerateSHA512String(string inputString)
        {
            using (SHA512 sha512Hash = SHA512.Create())
            {
                //From String to byte array
                byte[] sourceBytes = Encoding.UTF8.GetBytes(inputString);
                byte[] hashBytes = sha512Hash.ComputeHash(sourceBytes);
                string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                return hash;
            }
        }
        #endregion

        #region Properties

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture => false;

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund => false;

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund => true;

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid => false;

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo => false;

        ///// <summary>
        ///// Gets a payment method description that will be displayed on checkout pages in the public store
        ///// </summary>
        //public string PaymentMethodDescription => _localizationService.GetResource("Plugins.Payments.Worldline.PaymentMethodDescription");

        #endregion
    }
}