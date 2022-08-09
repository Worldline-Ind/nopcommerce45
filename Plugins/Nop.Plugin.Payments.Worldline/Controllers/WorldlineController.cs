using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Worldline.Models;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Factories;
using Nop.Web.Models.ShoppingCart;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
//using Nop.Core.Data;
using Nop.Services.Catalog;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Menu;
using Nop.Web.Areas.Admin.Models.Orders;
using Nop.Data;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Worldline.Controllers
{
    public class WorldlineController : BasePaymentController
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPaymentPluginManager _paymentPluginManager;
        private readonly IPermissionService _permissionService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly INotificationService _notificationService;
        private readonly ISettingService _settingService;
        private readonly IStoreContext _storeContext;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly IWebHostEnvironment _env;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
        private readonly IRepository<Order> _orderRepository;
        private readonly IProductService _productService;
        private readonly ICustomerActivityService _customerActivityService;
        #endregion

        #region Ctor

        public WorldlineController(IGenericAttributeService genericAttributeService, 
            IShoppingCartModelFactory shoppingCartModelFactory,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPaymentPluginManager paymentPluginManager,
            IPermissionService permissionService,
            ILocalizationService localizationService,
            ILogger logger,
            INotificationService notificationService,
            ISettingService settingService,
            IStoreContext storeContext,
            IWebHelper webHelper,
            IWorkContext workContext,
            IRepository<Order> orderRepository,
            IProductService productService,
            IWebHostEnvironment env, IShoppingCartService shoppingCartService,
            ICustomerActivityService customerActivityService,
            ShoppingCartSettings shoppingCartSettings)
        {
            _genericAttributeService = genericAttributeService;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _paymentPluginManager = paymentPluginManager;
            _permissionService = permissionService;
            _localizationService = localizationService;
            _logger = logger;
            _notificationService = notificationService;
            _settingService = settingService;
            _storeContext = storeContext;
            _webHelper = webHelper;
            _workContext = workContext;
            _shoppingCartSettings = shoppingCartSettings;
            _shoppingCartService = shoppingCartService;
            _shoppingCartModelFactory = shoppingCartModelFactory;
            _env = env;
            _orderRepository = orderRepository;
            _productService = productService;
            _customerActivityService = customerActivityService;
        }

        #endregion

        #region Utilities
        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task ProcessRecurringPaymentAsync(string invoiceId, PaymentStatus newPaymentStatus, string transactionId, string ipnInfo)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(invoiceId);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = await _orderService.GetOrderByGuidAsync(orderNumberGuid);
            if (order == null)
            {
                await _logger.ErrorAsync("Worldline IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            var recurringPayments = await _orderService.SearchRecurringPaymentsAsync(initialOrderId: order.Id);

            foreach (var rp in recurringPayments)
            {
                switch (newPaymentStatus)
                {
                    case PaymentStatus.Authorized:
                    case PaymentStatus.Paid:
                        {
                            var recurringPaymentHistory = await _orderService.GetRecurringPaymentHistoryAsync(rp);
                            if (!recurringPaymentHistory.Any())
                            {
                                await _orderService.InsertRecurringPaymentHistoryAsync(new RecurringPaymentHistory
                                {
                                    RecurringPaymentId = rp.Id,
                                    OrderId = order.Id,
                                    CreatedOnUtc = DateTime.UtcNow
                                });
                            }
                            else
                            {
                                //next payments
                                var processPaymentResult = new ProcessPaymentResult
                                {
                                    NewPaymentStatus = newPaymentStatus
                                };
                                if (newPaymentStatus == PaymentStatus.Authorized)
                                    processPaymentResult.AuthorizationTransactionId = transactionId;
                                else
                                    processPaymentResult.CaptureTransactionId = transactionId;

                                await _orderProcessingService.ProcessNextRecurringPaymentAsync(rp,
                                    processPaymentResult);
                            }
                        }

                        break;
                    case PaymentStatus.Voided:
                        //failed payment
                        var failedPaymentResult = new ProcessPaymentResult
                        {
                            Errors = new[] { $"Worldline IPN. Recurring payment is {nameof(PaymentStatus.Voided).ToLowerInvariant()} ." },
                            RecurringPaymentFailed = true
                        };
                        await _orderProcessingService.ProcessNextRecurringPaymentAsync(rp, failedPaymentResult);
                        break;
                }
            }

            //OrderService.InsertOrderNote(newOrder.OrderId, sb.ToString(), DateTime.UtcNow);
            await _logger.InformationAsync("Worldline IPN. Recurring info", new NopException(ipnInfo));
        }

        /// <returns>A task that represents the asynchronous operation</returns>
        protected virtual async Task ProcessPaymentAsync(string orderNumber, string ipnInfo, PaymentStatus newPaymentStatus, decimal mcGross, string transactionId)
        {
            Guid orderNumberGuid;

            try
            {
                orderNumberGuid = new Guid(orderNumber);
            }
            catch
            {
                orderNumberGuid = Guid.Empty;
            }

            var order = await _orderService.GetOrderByGuidAsync(orderNumberGuid);

            if (order == null)
            {
                await _logger.ErrorAsync("Worldline IPN. Order is not found", new NopException(ipnInfo));
                return;
            }

            //order note
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = order.Id,
                Note = ipnInfo,
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            });

            //validate order total
            if ((newPaymentStatus == PaymentStatus.Authorized || newPaymentStatus == PaymentStatus.Paid) && !Math.Round(mcGross, 2).Equals(Math.Round(order.OrderTotal, 2)))
            {
                var errorStr = $"Worldline IPN. Returned order total {mcGross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                //log
                await _logger.ErrorAsync(errorStr);
                //order note
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = order.Id,
                    Note = errorStr,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

                return;
            }

            switch (newPaymentStatus)
            {
                case PaymentStatus.Authorized:
                    if (_orderProcessingService.CanMarkOrderAsAuthorized(order))
                        await _orderProcessingService.MarkAsAuthorizedAsync(order);
                    break;
                case PaymentStatus.Paid:
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.AuthorizationTransactionId = transactionId;
                        await _orderService.UpdateOrderAsync(order);

                        await _orderProcessingService.MarkOrderAsPaidAsync(order);
                    }

                    break;
                case PaymentStatus.Refunded:
                    var totalToRefund = Math.Abs(mcGross);
                    if (totalToRefund > 0 && Math.Round(totalToRefund, 2).Equals(Math.Round(order.OrderTotal, 2)))
                    {
                        //refund
                        if (_orderProcessingService.CanRefundOffline(order))
                            await _orderProcessingService.RefundOfflineAsync(order);
                    }
                    else
                    {
                        //partial refund
                        if (_orderProcessingService.CanPartiallyRefundOffline(order, totalToRefund))
                            await _orderProcessingService.PartiallyRefundOfflineAsync(order, totalToRefund);
                    }

                    break;
                case PaymentStatus.Voided:
                    if (_orderProcessingService.CanVoidOffline(order))
                        await _orderProcessingService.VoidOfflineAsync(order);

                    break;
            }
        }

        #endregion

        #region Methods

        [HttpPost]
        public async Task<ActionResult> ResponseHandler(IFormCollection formCollection)
        {
            try
            {
                foreach (var key in formCollection.Keys)
                {
                    var value = formCollection[key];
                }
                string path = _env.WebRootPath;
                string json = string.Empty;
                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    json = r.ReadToEnd();
                    r.Close();
                }
                var merchantcode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode");
                var currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency");
                //   JObject config_data = JObject.Parse(json);
                var data = formCollection["msg"].ToString().Split('|');
                if (data == null)
                {//|| data[1].ToString()== "User Aborted"
                    ViewBag.abrt = true;
                    //return Redirect(ControllerContext.HttpContext.Request.UrlReferrer.ToString());
                    //string referer = Request.Headers["Referer"].ToString();
                    //RequestHeaders header = Request.GetTypedHeaders();
                    //Uri uriReferer = header.Referer;
                    string referer = Request.Headers["Referer"].ToString();
                    return Redirect(referer);
                }
                ViewBag.online_transaction_msg = data;
                if (data[0] == "0300")
                {
                    ViewBag.abrt = false;
                    var strJ = new
                    {
                        merchant = new
                        {
                            identifier = merchantcode.Result.Value //config_data["merchantCode"].ToString()
                        },
                        transaction = new
                        {
                            deviceIdentifier = "S",
                            currency = currency.Result.Value, //config_data["currency"],
                            dateTime = string.Format("{0:d/M/yyyy}", data[8].ToString()),
                            token = data[5].ToString(),
                            requestType = "S"
                        }
                    };
                    HttpContent content = new StringContent(JsonConvert.SerializeObject(strJ));
                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    HttpResponseMessage response = client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", content).Result;
                    var a = response.Content.ReadAsStringAsync();

                    JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(a));
                    var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

                    List<JToken> tokens = jsonData.Children().ToList();

                    var jsonData1 = JObject.Parse(tokens[6].ToString()).Children();
                    List<JToken> tokens1 = jsonData.Children().ToList();
                    var cart = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.ShoppingCart, _storeContext.GetCurrentStoreAsync().Result.Id);
                    OrderTotalsModel modelTtl = await _shoppingCartModelFactory.PrepareOrderTotalsModelAsync(cart, false);
                    var model = new ShoppingCartModel();
                    model = await _shoppingCartModelFactory.PrepareShoppingCartModelAsync(new ShoppingCartModel(), cart,
                        isEditable: false, prepareAndDisplayOrderReviewData: true);
                    //  var model1 = _shoppingCartModelFactory.pre(cart, false);

                    Order order = new Order();
                    order.CustomerId = _workContext.GetCurrentCustomerAsync().Result.Id;
                    order.AuthorizationTransactionId = data[5];
                    order.AuthorizationTransactionCode = data[3];
                    order.AuthorizationTransactionResult = tokens[6]["paymentTransaction"]["statusMessage"].ToString();
                    order.CaptureTransactionResult = formCollection["msg"].ToString();                    
                    order.PaymentStatusId = (tokens[6]["paymentTransaction"]["statusMessage"].ToString() == "SUCCESS") ? 30 : 10;                    
                    order.StoreId = _storeContext.GetCurrentStoreAsync().Result.Id;
                    order.BillingAddressId = (int)(_workContext.GetCurrentCustomerAsync().Result.BillingAddressId);
                    order.PickupInStore = model.OrderReviewData.SelectedPickupInStore;
                    order.OrderStatusId = 10;
                    order.ShippingStatusId = 20;
                    order.PaymentMethodSystemName = "Payments.Worldline";
                    order.CustomerCurrencyCode = "INR";
                    order.CurrencyRate = _workContext.GetWorkingCurrencyAsync().Result.Rate;
                    order.CustomerTaxDisplayTypeId = 10;
                    order.OrderSubtotalInclTax = String.IsNullOrEmpty(modelTtl.SubTotal) ? 0.00m : Convert.ToDecimal(modelTtl.SubTotal.Substring(1)) + Convert.ToDecimal(String.IsNullOrEmpty(modelTtl.Tax) ? 0.00m : modelTtl.Tax.Substring(1));
                    order.OrderSubtotalExclTax = String.IsNullOrEmpty(modelTtl.SubTotal) ? 0.00m : Convert.ToDecimal(modelTtl.SubTotal.Substring(1));
                    order.OrderSubTotalDiscountInclTax = 0.00m;
                    order.OrderSubTotalDiscountExclTax = 0.00m;
                    order.OrderShippingInclTax = Convert.ToDecimal(String.IsNullOrEmpty(modelTtl.Shipping) ? 0.00m : modelTtl.Shipping.Substring(1));
                    order.OrderShippingExclTax = 0.00m;
                    order.PaymentMethodAdditionalFeeInclTax = 0.00m;
                    order.PaymentMethodAdditionalFeeExclTax = 0.00m;
                    order.TaxRates = modelTtl.TaxRates.ToString();
                    order.OrderTax = 0.00m;
                    order.OrderDiscount = 0.00m;
                    order.OrderTotal = Convert.ToDecimal(String.IsNullOrEmpty(modelTtl.OrderTotal) ? 0.00m : modelTtl.OrderTotal.Substring(1));
                    order.RefundedAmount = 0.00m;
                    order.CustomerLanguageId = _storeContext.GetCurrentStoreAsync().Result.DefaultLanguageId;
                    order.AffiliateId = _workContext.GetCurrentCustomerAsync().Result.AffiliateId;
                    order.AllowStoringCreditCardNumber = false;

                    order.Deleted = false;
                    order.ShippingAddressId = model.OrderReviewData.ShippingAddress.Id;
                    order.CreatedOnUtc = DateTime.UtcNow;
                    //order.CustomOrderNumber
                    order.OrderGuid = Guid.NewGuid();

                    var last = _orderRepository.Table.OrderByDescending(p => p.Id).First();
                    int custOrdnum = last.Id + 1;
                    order.CustomOrderNumber = custOrdnum.ToString();
                    await _orderService.InsertOrderAsync(order);
                    if (_workContext.GetCurrentCustomerAsync().Result.HasShoppingCartItems)
                    {
                        var shoppingCartItems = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.ShoppingCart);
                        //.Sum(item => item.Quantity);
                        foreach (var item in shoppingCartItems)
                        {
                            //_shoppingCartService.DeleteShoppingCartItem(item.Id);
                            var product = _productService.GetProductByIdAsync(item.ProductId);
                            OrderItem newOrderItem = new OrderItem
                            {
                                OrderItemGuid = Guid.NewGuid(),
                                OrderId = order.Id,
                                ProductId = item.ProductId,
                                UnitPriceInclTax = product.Result.Price,
                                UnitPriceExclTax = product.Result.Price,
                                PriceInclTax = product.Result.Price * item.Quantity,
                                PriceExclTax = product.Result.Price * item.Quantity,
                                OriginalProductCost = product.Result.ProductCost,
                                AttributeDescription = "",
                                AttributesXml = item.AttributesXml,
                                Quantity = item.Quantity,
                                DiscountAmountInclTax = Convert.ToDecimal(0.00),
                                DiscountAmountExclTax = Convert.ToDecimal(0.00),
                                DownloadCount = 0,
                                IsDownloadActivated = product.Result.IsDownload,
                                LicenseDownloadId = product.Result.DownloadId,
                                ItemWeight = product.Result.Weight,
                                RentalStartDateUtc = item.RentalStartDateUtc,
                                RentalEndDateUtc = item.RentalEndDateUtc
                            };
                            //order.OrderItems.Add(newOrderItem);
                            await _orderService.InsertOrderItemAsync(newOrderItem);
                        }
                        
                        List<int> ids = new List<int>();


                        foreach (var item in shoppingCartItems)
                        {
                            ids.Add(item.Id);
                            //  _shoppingCartService.DeleteShoppingCartItem(item.Id);
                        }
                        foreach (var item in ids)
                        {
                            await _shoppingCartService.DeleteShoppingCartItemAsync(item);

                        }
                        _notificationService.SuccessNotification("Order Placed successfully!");

                    }

                    //int itemCnt = _workContext.CurrentCustomer.ShoppingCartItems.Count;
                    //for (int i = 0; i < itemCnt; i++)
                    //{

                    //}

                    //ViewBag.dual_verification_result = dual_verification_result;
                    //ViewBag.a = a;
                    //ViewBag.jsonData = jsonData;
                    //ViewBag.tokens = tokens;
                    //ViewBag.paramsData = formCollection["msg"];

                    // return response;
                }

            }
            catch (Exception ex)
            {

                //throw;
            }
            return RedirectToAction("Index", "Home");
            //  return ViewComponent("ResponseHandler", new { formCollection = formCollection });
            //  return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
            //   return Content("Success");
            //return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
            //  return Redirect(_storeContext.CurrentStore.Url+ "checkout/OpcSavePaymentInfo");
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var worldlinePaymentSettings = await _settingService.LoadSettingAsync<WorldlinePaymentSettings>(storeScope);


            var model = new ConfigurationModel
            {
                Utf8 = worldlinePaymentSettings.utf8,
                Authenticity_token = worldlinePaymentSettings.authenticity_token,
                MerchantCode = worldlinePaymentSettings.merchantCode,
                MerchantSchemeCode = worldlinePaymentSettings.merchantSchemeCode,
                SALT = worldlinePaymentSettings.SALT,
                Currency = worldlinePaymentSettings.currency,
                TypeOfPayment = worldlinePaymentSettings.typeOfPayment,
                PrimaryColor = worldlinePaymentSettings.primaryColor,
                SecondaryColor = worldlinePaymentSettings.secondaryColor,
                ButtonColor1 = worldlinePaymentSettings.buttonColor1,
                ButtonColor2 = worldlinePaymentSettings.buttonColor2,
                LogoURL = worldlinePaymentSettings.logoURL,
                EnableExpressPay = worldlinePaymentSettings.enableExpressPay,
                SeparateCardMode = worldlinePaymentSettings.separateCardMode,
                EnableNewWindowFlow = worldlinePaymentSettings.enableNewWindowFlow,
                MerchantMessage = worldlinePaymentSettings.merchantMessage,
                DisclaimerMessage = worldlinePaymentSettings.disclaimerMessage,
                PaymentMode = worldlinePaymentSettings.paymentMode,
                PaymentModeOrder = worldlinePaymentSettings.paymentModeOrder,
                EnableInstrumentDeRegistration = worldlinePaymentSettings.enableInstrumentDeRegistration,
                TransactionType = worldlinePaymentSettings.transactionType,
                HideSavedInstruments = worldlinePaymentSettings.hideSavedInstruments,
                SaveInstrument = worldlinePaymentSettings.saveInstrument,
                DisplayTransactionMessageOnPopup = worldlinePaymentSettings.displayTransactionMessageOnPopup,
                EmbedPaymentGatewayOnPage = worldlinePaymentSettings.embedPaymentGatewayOnPage,
                EnableSI = worldlinePaymentSettings.enableSI,
                HideSIDetails = worldlinePaymentSettings.hideSIDetails,
                HideSIConfirmation = worldlinePaymentSettings.hideSIConfirmation,
                ExpandSIDetails = worldlinePaymentSettings.expandSIDetails,
                EnableDebitDay = worldlinePaymentSettings.enableDebitDay,
                ShowSIResponseMsg = worldlinePaymentSettings.showSIResponseMsg,
                ShowSIConfirmation = worldlinePaymentSettings.showSIConfirmation,
                EnableTxnForNonSICards = worldlinePaymentSettings.enableTxnForNonSICards,
                ShowAllModesWithSI = worldlinePaymentSettings.showAllModesWithSI,
                SiDetailsAtMerchantEnd = worldlinePaymentSettings.siDetailsAtMerchantEnd,
                AmountType = worldlinePaymentSettings.amounttype,
                Frequency = worldlinePaymentSettings.frequency,
                //merchantLogoUrl = worldlinePaymentSettings.merchantLogoUrl,
                //merchantMsg = worldlinePaymentSettings.merchantMsg,
                //disclaimerMsg = worldlinePaymentSettings.disclaimerMsg,
                ShowPGResponseMsg = worldlinePaymentSettings.showPGResponseMsg,
                EnableAbortResponse = worldlinePaymentSettings.enableAbortResponse,
                //UseSandbox = worldlinePaymentSettings.UseSandbox,
                //BusinessEmail = worldlinePaymentSettings.BusinessEmail,
                //PdtToken = worldlinePaymentSettings.PdtToken,
                //PassProductNamesAndTotals = worldlinePaymentSettings.PassProductNamesAndTotals,
                //AdditionalFee = worldlinePaymentSettings.AdditionalFee,
                //AdditionalFeePercentage = worldlinePaymentSettings.AdditionalFeePercentage,
                ActiveStoreScopeConfiguration = storeScope
            };

            List<SelectListItem> enbDisb = new List<SelectListItem>()
            {
                new SelectListItem() {Text="Enable", Value="true"},
                new SelectListItem() { Text="Disable", Value="false"}
            };

            List<SelectListItem> currencyCodes = new List<SelectListItem>()
            {
                new SelectListItem() {Text="INR", Value="INR"},
                new SelectListItem() { Text="USD", Value="USD"}
            };

            List<SelectListItem> paymentMode = new List<SelectListItem>()
            {
        new SelectListItem() {Text="all", Value="all"},
        new SelectListItem() { Text="cards", Value="cards"},
         new SelectListItem() {Text="netBanking", Value="netBanking"},
        new SelectListItem() { Text="UPI", Value="UPI"},
         new SelectListItem() {Text="imps", Value="imps"},
        new SelectListItem() { Text="wallets", Value="wallets"},
         new SelectListItem() {Text="cashCards", Value="cashCards"},
        new SelectListItem() { Text="NEFTRTGS", Value="NEFTRTGS"},
          new SelectListItem() { Text="emiBanks", Value="emiBanks"}
            };

            List<SelectListItem> typeOfPayment = new List<SelectListItem>()
            {
        new SelectListItem() {Text="TEST", Value="TEST"},
        new SelectListItem() { Text="LIVE", Value="LIVE"}

            };
            List<SelectListItem> amounttype = new List<SelectListItem>()
            {
        new SelectListItem() { Text="Variable", Value="Variable"},
        new SelectListItem() {Text="Fixed", Value="Fixed"}
            };
            List<SelectListItem> frequency = new List<SelectListItem>()
            {
        new SelectListItem() {Text="As and when presented", Value="ADHO"},
        new SelectListItem() {Text="Daily", Value="DAIL"},
        new SelectListItem() {Text="Weekly", Value="WEEK"},
        new SelectListItem() {Text="Monthly", Value="MNTH"},
        new SelectListItem() {Text="Quarterly", Value="QURT"},
        new SelectListItem() {Text="Semi annually", Value="MIAN"},
        new SelectListItem() {Text="Yearly", Value="YEAR"},
        new SelectListItem() {Text="Bi- monthly", Value="BIMN"}
            };
            List<SelectListItem> transactionTypes = new List<SelectListItem>()
            {
        new SelectListItem() { Text="SALE", Value="SALE"}

            };

            ViewBag.enbDisb = enbDisb;
            ViewBag.currencyCodes = currencyCodes;
            ViewBag.paymentModes = paymentMode;
            ViewBag.typeOfPayment = typeOfPayment;
            ViewBag.amounttype = amounttype;
            ViewBag.frequency = frequency;
            ViewBag.transactionTypes = transactionTypes;



            if (storeScope <= 0)

                return View("~/Plugins/Payments.Worldline/Views/Configure.cshtml", model);

            model.Utf8_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.utf8, storeScope);
            model.Authenticity_token_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.authenticity_token, storeScope);

            model.MerchantCode_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.merchantCode, storeScope);
            model.MerchantSchemeCode_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.merchantSchemeCode, storeScope);
            model.SALT_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.SALT, storeScope);
            model.Currency_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.currency, storeScope);
            model.TypeOfPayment_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.typeOfPayment, storeScope);
            model.PrimaryColor_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.primaryColor, storeScope);
            model.SecondaryColor_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.secondaryColor, storeScope);
            model.ButtonColor1_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.buttonColor1, storeScope);
            model.ButtonColor2_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.buttonColor2, storeScope);
            model.LogoURL_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.logoURL, storeScope);
            model.EnableExpressPay_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableExpressPay, storeScope);
            model.SeparateCardMode_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.separateCardMode, storeScope);
            model.EnableNewWindowFlow_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableNewWindowFlow, storeScope);
            model.MerchantMessage_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.merchantMessage, storeScope);
            model.DisclaimerMessage_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.disclaimerMessage, storeScope);
            model.PaymentMode_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.paymentMode, storeScope);
            model.PaymentModeOrder_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.paymentModeOrder, storeScope);
            model.EnableInstrumentDeRegistration_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableInstrumentDeRegistration, storeScope);
            model.TransactionType_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.transactionType, storeScope);
            model.HideSavedInstruments_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.hideSavedInstruments, storeScope);
            model.SaveInstrument_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.saveInstrument, storeScope);
            model.DisplayTransactionMessageOnPopup_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.displayTransactionMessageOnPopup, storeScope);
            model.EmbedPaymentGatewayOnPage_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.embedPaymentGatewayOnPage, storeScope);
            model.EnableSI_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableSI, storeScope);
            model.HideSIDetails_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.hideSIDetails, storeScope);
            model.HideSIConfirmation_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.hideSIConfirmation, storeScope);
            model.ExpandSIDetails_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.expandSIDetails, storeScope);
            model.EnableDebitDay_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableDebitDay, storeScope);
            model.ShowSIResponseMsg_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.showSIResponseMsg, storeScope);
            model.ShowSIConfirmation_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.showSIConfirmation, storeScope);
            model.EnableTxnForNonSICards_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableTxnForNonSICards, storeScope);
            model.ShowAllModesWithSI_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.showAllModesWithSI, storeScope);
            model.SiDetailsAtMerchantEnd_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.siDetailsAtMerchantEnd, storeScope);
            model.AmountType_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.amounttype, storeScope);
            model.Frequency_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.frequency, storeScope);

            //  model.merchantLogoUrl_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.merchantLogoUrl, storeScope);
            // model.merchantMsg_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.merchantMsg, storeScope);
            //model.disclaimerMsg_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.disclaimerMsg, storeScope);
            model.ShowPGResponseMsg_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.showPGResponseMsg, storeScope);
            model.EnableAbortResponse_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.enableAbortResponse, storeScope);


            //model.UseSandbox_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.UseSandbox, storeScope);
            //model.BusinessEmail_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.BusinessEmail, storeScope);
            //model.PdtToken_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.PdtToken, storeScope);
            //model.PassProductNamesAndTotals_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.PassProductNamesAndTotals, storeScope);
            //model.AdditionalFee_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.AdditionalFee, storeScope);
            //model.AdditionalFeePercentage_OverrideForStore = await _settingService.SettingExistsAsync(worldlinePaymentSettings, x => x.AdditionalFeePercentage, storeScope);

            return View("~/Plugins/Payments.Worldline/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        //[AdminAntiForgery]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var worldlinePaymentSettings = await _settingService.LoadSettingAsync<WorldlinePaymentSettings>(storeScope);




            //save settings
            worldlinePaymentSettings.merchantCode = model.MerchantCode;
            worldlinePaymentSettings.merchantSchemeCode = model.MerchantSchemeCode;
            worldlinePaymentSettings.SALT = model.SALT;
            worldlinePaymentSettings.currency = model.Currency;
            worldlinePaymentSettings.typeOfPayment = model.TypeOfPayment;
            worldlinePaymentSettings.primaryColor = model.PrimaryColor;
            worldlinePaymentSettings.secondaryColor = model.SecondaryColor;
            worldlinePaymentSettings.buttonColor1 = model.ButtonColor1;
            worldlinePaymentSettings.buttonColor2 = model.ButtonColor2;
            worldlinePaymentSettings.logoURL = model.LogoURL;
            worldlinePaymentSettings.enableExpressPay = model.EnableExpressPay;
            worldlinePaymentSettings.separateCardMode = model.SeparateCardMode;
            worldlinePaymentSettings.enableNewWindowFlow = model.EnableNewWindowFlow;
            worldlinePaymentSettings.merchantMessage = model.MerchantMessage;
            worldlinePaymentSettings.disclaimerMessage = model.DisclaimerMessage;
            worldlinePaymentSettings.paymentMode = model.PaymentMode;
            worldlinePaymentSettings.paymentModeOrder = model.PaymentModeOrder;
            worldlinePaymentSettings.enableInstrumentDeRegistration = model.EnableInstrumentDeRegistration;
            worldlinePaymentSettings.transactionType = model.TransactionType;
            worldlinePaymentSettings.hideSavedInstruments = model.HideSavedInstruments;
            worldlinePaymentSettings.saveInstrument = model.SaveInstrument;
            worldlinePaymentSettings.displayTransactionMessageOnPopup = model.DisplayTransactionMessageOnPopup;
            worldlinePaymentSettings.embedPaymentGatewayOnPage = model.EmbedPaymentGatewayOnPage;
            worldlinePaymentSettings.enableSI = model.EnableSI;
            worldlinePaymentSettings.hideSIDetails = model.HideSIDetails;
            worldlinePaymentSettings.hideSIConfirmation = model.HideSIConfirmation;
            worldlinePaymentSettings.expandSIDetails = model.ExpandSIDetails;
            worldlinePaymentSettings.enableDebitDay = model.EnableDebitDay;
            worldlinePaymentSettings.showSIResponseMsg = model.ShowSIResponseMsg;
            worldlinePaymentSettings.showSIConfirmation = model.ShowSIConfirmation;
            worldlinePaymentSettings.enableTxnForNonSICards = model.EnableTxnForNonSICards;
            worldlinePaymentSettings.showAllModesWithSI = model.ShowAllModesWithSI;
            worldlinePaymentSettings.siDetailsAtMerchantEnd = model.SiDetailsAtMerchantEnd;
            worldlinePaymentSettings.amounttype = model.AmountType;
            worldlinePaymentSettings.utf8 = model.Utf8;
            worldlinePaymentSettings.authenticity_token = model.Authenticity_token;
            worldlinePaymentSettings.frequency = model.Frequency;

            //worldlinePaymentSettings.merchantLogoUrl = model.merchantLogoUrl;
            //worldlinePaymentSettings.merchantMsg = model.merchantMsg;
            //worldlinePaymentSettings.disclaimerMsg = model.disclaimerMsg;
            worldlinePaymentSettings.showPGResponseMsg = model.ShowPGResponseMsg;
            worldlinePaymentSettings.enableAbortResponse = model.EnableAbortResponse;
            //worldlinePaymentSettings.UseSandbox = model.UseSandbox;
            //worldlinePaymentSettings.BusinessEmail = model.BusinessEmail;
            //worldlinePaymentSettings.PdtToken = model.PdtToken;
            //worldlinePaymentSettings.PassProductNamesAndTotals = model.PassProductNamesAndTotals;
            //worldlinePaymentSettings.AdditionalFee = model.AdditionalFee;
            //worldlinePaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */



            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.merchantCode, model.MerchantCode_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.merchantSchemeCode, model.MerchantSchemeCode_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.SALT, model.SALT_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.currency, model.Currency_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.typeOfPayment, model.TypeOfPayment_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.primaryColor, model.PrimaryColor_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.secondaryColor, model.SecondaryColor_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.buttonColor1, model.ButtonColor1_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.buttonColor2, model.ButtonColor2_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.logoURL, model.LogoURL_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableExpressPay, model.EnableExpressPay_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.separateCardMode, model.SeparateCardMode_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableNewWindowFlow, model.EnableNewWindowFlow_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.merchantMessage, model.MerchantMessage_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.disclaimerMessage, model.DisclaimerMessage_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.paymentMode, model.PaymentMode_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.paymentModeOrder, model.PaymentModeOrder_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableInstrumentDeRegistration, model.EnableInstrumentDeRegistration_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.transactionType, model.TransactionType_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.hideSavedInstruments, model.HideSavedInstruments_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.saveInstrument, model.SaveInstrument_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.displayTransactionMessageOnPopup, model.DisplayTransactionMessageOnPopup_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.embedPaymentGatewayOnPage, model.EmbedPaymentGatewayOnPage_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableSI, model.EnableSI_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.hideSIDetails, model.HideSIDetails_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.hideSIConfirmation, model.HideSIConfirmation_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.expandSIDetails, model.ExpandSIDetails_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableDebitDay, model.EnableDebitDay_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.showSIResponseMsg, model.ShowSIResponseMsg_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.showSIConfirmation, model.ShowSIConfirmation_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableTxnForNonSICards, model.EnableTxnForNonSICards_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.showAllModesWithSI, model.ShowAllModesWithSI_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.siDetailsAtMerchantEnd, model.SiDetailsAtMerchantEnd_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.amounttype, model.AmountType_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.utf8, model.Utf8_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.authenticity_token, model.Authenticity_token_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.frequency, model.Frequency_OverrideForStore, storeScope, false);

            //await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.merchantLogoUrl, model.merchantLogoUrl_OverrideForStore, storeScope, false);
            //await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.merchantMsg, model.merchantMsg_OverrideForStore, storeScope, false);
            //await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.disclaimerMsg, model.disclaimerMsg_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.showPGResponseMsg, model.ShowPGResponseMsg_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.enableAbortResponse, model.EnableAbortResponse_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.UseSandbox, model.UseSandbox_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.BusinessEmail, model.BusinessEmail_OverrideForStore, storeScope, false);
            //await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.PdtToken, model.PdtToken_OverrideForStore, storeScope, false);
            //await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.PassProductNamesAndTotals, model.PassProductNamesAndTotals_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.AdditionalFee, model.AdditionalFee_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync(worldlinePaymentSettings, x => x.AdditionalFeePercentage, model.AdditionalFeePercentage_OverrideForStore, storeScope, false);

            //now clear settings cache
            await _settingService.ClearCacheAsync();
            SiteMapNode rootNode = new SiteMapNode();
            var menuItem = new SiteMapNode
            {
                SystemName = "Home",
                Title = "Home",
                ControllerName = "Home",
                ActionName = "Overview",
                Visible = true,
                RouteValues = new RouteValueDictionary() { { "area", "admin" } }
            };

            //var menuItem = new SiteMapNode()
            //{
            //    SystemName = "YourCustomSystemName",
            //    Title = "Plugin Title",
            //    ControllerName = "ControllerName",
            //    ActionName = "List",
            //    Visible = true,
            //    RouteValues = new RouteValueDictionary() { { "area", null } },
            //};
            var pluginNode = menuItem.ChildNodes.FirstOrDefault(x => x.SystemName == "Low stock");
            if (pluginNode != null)
                pluginNode.ChildNodes.Add(menuItem);

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }
        [Area(AreaNames.Admin)]
        public JsonResult GenerateSHA512String(string inputString)
        {
            using (SHA512 sha512Hash = SHA512.Create())
            {
                //From String to byte array
                byte[] sourceBytes = Encoding.UTF8.GetBytes(inputString);
                byte[] hashBytes = sha512Hash.ComputeHash(sourceBytes);
                string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);

                System.Security.Cryptography.SHA512Managed sha512 = new System.Security.Cryptography.SHA512Managed();

                Byte[] EncryptedSHA512 = sha512.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hash));

                sha512.Clear();

                var bts = Convert.ToBase64String(EncryptedSHA512);

                //return Json(hash, JsonRequestBehavior.AllowGet);
                return Json(hash, new Newtonsoft.Json.JsonSerializerSettings());
            }
        }
        //[AuthorizeAdmin]
        //[Area(AreaNames.Admin)]
        //public ActionResult Refund()

        //{
        //    try
        //    {
        //        string path = _env.WebRootPath;
        //        string tranId = GenerateRandomString(12);
        //        ViewBag.tranId = tranId;
        //        var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode");
        //        var currency = _settingService.GetSetting("worldlinepaymentsettings.merchantcode");
        //        ViewBag.merchantcode = merchantcode.Value.ToString();
        //        ViewBag.currency = currency.Value.ToString();

        //        //using (StreamReader r = new StreamReader(path + "\\output.json"))
        //        //{
        //        //    string json = r.ReadToEnd();


        //        //    ViewBag.config_data = json;
        //        //}
        //    }
        //    catch (Exception ex)
        //    {

        //        //       throw;
        //    }
        //    //"~/Plugins/Payments.Worldline/Views/Refund.cshtml"

        //    return View("~/Plugins/Payments.Worldline/Views/Refund.cshtml");
        //   // return View();
        //}


        //[AuthorizeAdmin]
        //[Area(AreaNames.Admin)]
        //[HttpPost]
        //public ActionResult Refund(IFormCollection fc)
        //{
        //    try
        //    {
        //        string path = _env.WebRootPath;
        //        string json = "";
        //        string merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();
        //        string currency = _settingService.GetSetting("worldlinepaymentsettings.currency").Value.ToString();
        //        ViewBag.merchantcode = merchantcode;
        //        ViewBag.currency = currency;
        //        //using (StreamReader r = new StreamReader(path + "\\output.json"))
        //        //{
        //        //    json = r.ReadToEnd();
        //        //    r.Close();
        //        //}
        //        //JObject config_data = JObject.Parse(json);
        //        DateTime start_date = DateTime.Parse(fc["inputDate"].ToString());
        //        var data = new
        //        {
        //            merchant = new { identifier = merchantcode },
        //            cart = new
        //            {
        //            },
        //            transaction = new
        //            {
        //                deviceIdentifier = "S",
        //                amount = fc["amount"].ToString(),
        //                currency = currency,
        //                dateTime = start_date.ToString("dd-MM-yyyy"),
        //                token = fc["token"].ToString(),
        //                //string.Format("{0:d/M/yyyy}", day.ToString()),
        //                requestType = "R"
        //            }
        //        };
        //        HttpClient client = new HttpClient();
        //        client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
        //        client.DefaultRequestHeaders.Accept.Clear();
        //        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        //        HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", data).Result;
        //        var respStr = response.Content.ReadAsStringAsync();
        //        //data = null;
        //        JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
        //        var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();
        //        List<JToken> tokens = jsonData.Children().ToList();
        //        string statusCode = tokens[6]["paymentTransaction"]["statusCode"].ToString();
        //        string amount = tokens[6]["paymentTransaction"]["amount"].ToString();

        //        if (statusCode == "0499") //Change the code as per requirement
        //        {
        //            Order order = _orderRepository.Table.Where(a => a.AuthorizationTransactionId == fc["token"].ToString()).ToList().FirstOrDefault();
        //            order.PaymentStatusId = 40;
        //            _orderService.UpdateOrder(order);
        //            order.OrderNotes.Add(new OrderNote
        //            {
        //                Note = "Order has been marked as refunded. Amount = " + amount + "",
        //                DisplayToCustomer = false,
        //                CreatedOnUtc = DateTime.UtcNow
        //            });
        //            _orderService.UpdateOrder(order);
        //            _customerActivityService.InsertActivity("EditOrder",
        //            string.Format(_localizationService.GetResource("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
        //        }
        //        ////
        //        ViewBag.Tokens = tokens;

        //    }
        //    catch (Exception ex)
        //    {
        //        //   throw;
        //    }
        //    return View("~/Plugins/Payments.Worldline/Views/Refund.cshtml");
        //}       

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public ActionResult Reconcile()
        {
            return View("~/Plugins/Payments.Worldline/Views/Reconcile.cshtml");
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [HttpPost]
        public async Task<ActionResult> Reconcile(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;
                string json = "";
                var merchantcode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode").Result.Value.ToString();
                var currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency").Result.Value.ToString();
                ViewBag.merchantcode = merchantcode;
                ViewBag.currency = currency;
                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{
                //    json = r.ReadToEnd();
                //    r.Close();
                //}
                // JObject config_data = JObject.Parse(json);
                OrderSearchModel searchModel = new OrderSearchModel();
                searchModel.StoreId = 0;
                //var transaction_ids = getAuthorizationTransactionId(searchModel);
                //var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
                //List<JToken> tokens = new List<JToken>();
                var transDetails = new List<object>();
                //DateTime start_date = DateTime.ParseExact(fc["fromDate"].ToString(), "dd-mm-yyyy", CultureInfo.InvariantCulture);
                //DateTime end_date = DateTime.ParseExact(fc["endDate"], "dd-mm-yyyy", CultureInfo.InvariantCulture);
                DateTime start_date = DateTime.Parse(fc["fromDate"].ToString());
                DateTime end_date = DateTime.Parse(fc["endDate"].ToString());
                var diff = (end_date - start_date).TotalDays;
                end_date = end_date.AddDays(1);
                List<Order> lstOrder = _orderRepository.Table.Where(a => a.CreatedOnUtc <= end_date && a.CreatedOnUtc >= start_date && a.AuthorizationTransactionCode != null).ToList();
                foreach (Order order in lstOrder)
                {
                    int cntK = 0;
                    var authorizationTransactionCode = order.AuthorizationTransactionCode;
                    var day = order.CreatedOnUtc;
                    //for (var day = start_date; day <= end_date; day = day.AddDays(1))
                    //{
                    var data = new
                    {
                        merchant = new { identifier = merchantcode },
                        transaction = new
                        {
                            deviceIdentifier = "S",
                            currency = currency,
                            identifier = authorizationTransactionCode,
                            dateTime = day.ToString("dd-M-yyyy"),
                            //string.Format("{0:d/M/yyyy}", day.ToString()),
                            requestType = "O"
                        }
                    };
                    HttpContent content = new StringContent(JsonConvert.SerializeObject(data));
                    HttpClient client = new HttpClient();
                    client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    HttpResponseMessage response = client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", content).Result;
                    var respStr = response.Content.ReadAsStringAsync();
                    data = null;
                    JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
                    var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();
                    List<JToken> tokens = jsonData.Children().ToList();
                    if (tokens[6]["paymentTransaction"]["errorMessage"].ToString() == "Transaction Not Found")
                    {
                        cntK = cntK + 1;
                        if (cntK == diff)
                        {
                            transDetails.Add(tokens);
                            ;
                            tokens = null;
                        }
                        //  break;
                    }
                    else
                    {
                        ////
                        if (order.PaymentStatusId != 30)
                        {
                            string amount = tokens[6]["paymentTransaction"]["amount"].ToString();
                            order.PaymentStatusId = 30;
                            await _orderService.UpdateOrderAsync(order);
                            var orderNote = new OrderNote
                            {
                                DisplayToCustomer = false,
                                Note = "Order has been marked as Paid.Amount = " + amount + "",
                                CreatedOnUtc = DateTime.UtcNow
                            };

                            await _orderService.InsertOrderNoteAsync(orderNote);

                            //await _orderService.UpdateOrderAsync(order);
                            await _customerActivityService.InsertActivityAsync("EditOrder",
                                string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
                        }
                        ///
                        transDetails.Add(tokens);
                        ;
                        tokens = null;
                        //break;
                    }
                    //}
                }
                ViewBag.transDetails = transDetails;
            }
            catch (Exception ex)
            {
                //   throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/Reconcile.cshtml");
        }

        //[AuthorizeAdmin]
        //[Area(AreaNames.Admin)]
        //[HttpPost]
        //public ActionResult Reconcile(IFormCollection fc)
        //{
        //    try
        //    {
        //        string path = _env.WebRootPath;

        //        string json = "";

        //        var merchantcode = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

        //        var currency = _settingService.GetSetting("worldlinepaymentsettings.merchantcode").Value.ToString();

        //        ViewBag.merchantcode = merchantcode;
        //        ViewBag.currency = currency;


        //        //using (StreamReader r = new StreamReader(path + "\\output.json"))
        //        //{


        //        //    json = r.ReadToEnd();

        //        //    r.Close();

        //        //}

        //       // JObject config_data = JObject.Parse(json);
        //        var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
        //        //List<JToken> tokens = new List<JToken>();
        //        var transDetails = new List<object>();
        //        //DateTime start_date = DateTime.ParseExact(fc["fromDate"].ToString(), "dd-mm-yyyy", CultureInfo.InvariantCulture);
        //        //DateTime end_date = DateTime.ParseExact(fc["endDate"], "dd-mm-yyyy", CultureInfo.InvariantCulture);
        //        DateTime start_date = DateTime.Parse(fc["fromDate"].ToString());
        //        DateTime end_date = DateTime.Parse(fc["endDate"].ToString());
        //        var diff = (end_date - start_date).TotalDays;

        //        foreach (var transaction_id in transaction_ids)
        //        {
        //            int cntK = 0;
        //            for (var day = start_date; day <= end_date; day = day.AddDays(1))
        //            {
        //                var data = new
        //                {
        //                    merchant = new { identifier = merchantcode },
        //                    transaction = new
        //                    {
        //                        deviceIdentifier = "S",
        //                        currency = currency,
        //                        identifier = transaction_id,
        //                        dateTime = day.ToString("dd-M-yyyy"),
        //                        //string.Format("{0:d/M/yyyy}", day.ToString()),
        //                        requestType = "O"

        //                    }

        //                };

        //                HttpClient client = new HttpClient();
        //                client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
        //                client.DefaultRequestHeaders.Accept.Clear();
        //                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        //                HttpResponseMessage response = client.PostAsJsonAsync("https://www.paynimo.com/api/paynimoV2.req", data).Result;
        //                var respStr = response.Content.ReadAsStringAsync();

        //                data = null;
        //                JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
        //                var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

        //                List<JToken> tokens = jsonData.Children().ToList();
        //                if (tokens[6]["paymentTransaction"]["errorMessage"].ToString() == "Transactionn Not Found")
        //                {
        //                    cntK = cntK + 1;
        //                    if (cntK == diff)
        //                    {
        //                        transDetails.Add(tokens);
        //                        ;
        //                        tokens = null;
        //                    }

        //                    //  break;

        //                }
        //                else
        //                {
        //                    transDetails.Add(tokens);
        //                    ;
        //                    tokens = null;
        //                    break;
        //                }

        //            }
        //        }
        //        ViewBag.transDetails = transDetails;

        //    }
        //    catch (Exception ex)
        //    {

        //        //   throw;
        //    }

        //    return View("~/Plugins/Payments.Worldline/Views/Reconcile.cshtml");
        //}
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public ActionResult OfflineVerification()
        {
            try
            {
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;
                var merchantcode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode").Result.Value.ToString();

                var currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency").Result.Value.ToString();

                ViewBag.merchantcode = merchantcode.ToString();
                ViewBag.currency = currency.ToString();
                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    string json = r.ReadToEnd();
                    ViewBag.config_data = json;
                }
            }
            catch (Exception ex)
            {
                // throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/OfflineVerification.cshtml");
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [HttpPost]
        public ActionResult OfflineVerification(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;
                string json = "";
                string merchantcode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode").Result.Value;
                string currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency").Result.Value;
                ViewBag.merchantcode = merchantcode;
                ViewBag.currency = currency;
                DateTime start_date = DateTime.Parse(fc["date"].ToString());
                var data = new
                {
                    merchant = new { identifier = merchantcode },
                    transaction = new
                    {
                        deviceIdentifier = "S",
                        currency = currency,
                        identifier = fc["merchantRefNo"].ToString(),
                        dateTime = start_date.ToString("dd-M-yyyy"),
                        //string.Format("{0:d/M/yyyy}", day.ToString()),
                        requestType = "O"
                    }
                };
                HttpContent content = new StringContent(JsonConvert.SerializeObject(data));
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", content).Result;
                var respStr = response.Content.ReadAsStringAsync();

                data = null;
                JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
                var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

                List<JToken> tokens = jsonData.Children().ToList();
                ViewBag.Tokens = tokens;
                //var transaction_ids = fc["merchantRefNo"].ToString().Replace(System.Environment.NewLine, "").Replace(" ", "").Split(',');
                //List<JToken> tokens = new List<JToken>();
                var transDetails = new List<object>();
            }
            catch (Exception ex)
            {
                //   throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/OfflineVerification.cshtml");
        }
        
        public async Task<ActionResult> s2s(string msg)
        {
            try
            {
                //string json = string.Empty;
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;

                var data = msg.Split('|');
                ViewBag.clnt_txn_ref = data[3];
                ViewBag.pg_txn_id = data[5];
                //var dejson=JsonConvert.DeserializeObject(json);
                //JavaScriptSerializer js = new JavaScriptSerializer();
                //JsonSerializer js = new JsonSerializer();  //Added_N 
                //string jtRead=js.Serialize(json,);

                //dynamic dejson = js.Deserialize<dynamic>(json);

                //var dejson = JsonConvert.DeserializeObject<dynamic>(json);
                // dynamic dejson = js.Deserialize<dynamic>(jsonTextReader);

                StringBuilder res = new StringBuilder();
                for (int i = 0; i < data.Length - 1; i++)
                {
                    res.Append(data[i] + "|");
                }
                var salt = _settingService.GetSettingAsync("worldlinepaymentsettings.SALT").Result.Value.ToString();

                string data_string = res.ToString() + salt;// dejson["SALT"];
                var hash = GenerateSHA512StringFors2s(data_string);
                //var hash = GenerateSHA512StringFors2s(data_string);
                //if (data[15].ToString() == hash.Data.ToString().ToLower())
                //if (data[15].ToString() == hash.Value.ToString().ToLower())
                //{
                //    ViewBag.status = "1";
                //}
                //else
                //{
                //    ViewBag.status = "0";
                //}
                if (data[15].ToString() == hash.Value.ToString().ToLower())
                {
                    ViewBag.status = "1";
                    Order order = _orderRepository.Table.Where(a => a.AuthorizationTransactionId == data[5].ToString()).FirstOrDefault();
                    //
                    if (order.PaymentStatusId != 30)
                    {
                        string amount = data[6].ToString();
                        order.PaymentStatusId = 30;
                        await _orderService.UpdateOrderAsync(order);
                        OrderNote objOrderNote = new OrderNote
                        {
                            Note = "Order has been marked as Paid. Amount = " + amount + "",
                            DisplayToCustomer = false,
                            CreatedOnUtc = DateTime.UtcNow,
                            OrderId = order.Id
                        };
                        await _orderService.InsertOrderNoteAsync(objOrderNote);
                        //await _orderService.UpdateOrderAsync(order);
                        await _customerActivityService.InsertActivityAsync("EditOrder",
                            string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
                    }
                    //
                }
                else
                {
                    ViewBag.status = "0";
                }
            }
            catch (Exception ex)
            {
                //throw ex;
            }
            return View("~/Plugins/Payments.Worldline/Views/s2s.cshtml");
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<ActionResult> RefundAsync()

        {
            try
            {
                string path = _env.WebRootPath;
                string tranId = GenerateRandomString(12);
                ViewBag.tranId = tranId;
                var merchantcode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode").Result.Value.ToString();
                var currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency").Result.Value.ToString();
                ViewBag.merchantcode = merchantcode.ToString();
                ViewBag.currency = currency.ToString();
            }
            catch (Exception ex)
            {

                //       throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/Refund.cshtml");
            // return View();
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        [HttpPost]
        public async Task<ActionResult> RefundAsync(IFormCollection fc)
        {
            try
            {
                string path = _env.WebRootPath;
                string json = string.Empty;
                string merchantcode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode").Result.Value.ToString();
                string currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency").Result.Value.ToString();
                ViewBag.merchantcode = merchantcode;
                ViewBag.currency = currency;
                //using (StreamReader r = new StreamReader(path + "\\output.json"))
                //{
                //    json = r.ReadToEnd();
                //    r.Close();
                //}
                //JObject config_data = JObject.Parse(json);
                DateTime start_date = DateTime.Parse(fc["inputDate"].ToString());
                var data = new
                {
                    merchant = new { identifier = merchantcode },
                    cart = new
                    {
                    },
                    transaction = new
                    {
                        deviceIdentifier = "S",
                        amount = fc["amount"].ToString(),
                        currency = currency,
                        dateTime = start_date.ToString("dd-MM-yyyy"),
                        token = fc["token"].ToString(),
                        //string.Format("{0:d/M/yyyy}", day.ToString()),
                        requestType = "R"
                    }
                };
                HttpContent content = new StringContent(JsonConvert.SerializeObject(data));
                HttpClient client = new HttpClient();
                client.BaseAddress = new Uri("https://www.paynimo.com/api/paynimoV2.req");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                HttpResponseMessage response = client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", content).Result;
                var respStr = response.Content.ReadAsStringAsync();
                //data = null;
                JObject dual_verification_result = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(respStr));
                var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();
                List<JToken> tokens = jsonData.Children().ToList();
                string statusCode = tokens[6]["paymentTransaction"]["statusCode"].ToString();
                string amount = tokens[6]["paymentTransaction"]["amount"].ToString();

                if (statusCode == "0499") //Change the code as per requirement
                {
                    Order order = _orderRepository.Table.Where(a => a.AuthorizationTransactionId == fc["token"].ToString()).ToList().FirstOrDefault();
                    order.PaymentStatusId = 40;
                    await _orderService.UpdateOrderAsync(order);
                    OrderNote objOrderNote = new OrderNote
                    {
                        Note = "Order has been marked as refunded. Amount = " + amount + "",
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow,
                        OrderId = order.Id
                    };
                    await _orderService.InsertOrderNoteAsync(objOrderNote);
                    //_orderService.UpdateOrder(order);
                    await _customerActivityService.InsertActivityAsync("EditOrder",
                    string.Format(await _localizationService.GetResourceAsync("ActivityLog.EditOrder"), order.CustomOrderNumber), order);
                }
                ViewBag.Tokens = tokens;
            }
            catch (Exception ex)
            {
                //   throw;
            }
            return View("~/Plugins/Payments.Worldline/Views/Refund.cshtml");
        }

        public static string GenerateRandomString(int size)
        {
            Guid g = Guid.NewGuid();

            string random1 = g.ToString();
            random1 = random1.Replace("-", "");
            var builder = random1.Substring(0, size);
            return builder.ToString();
        }
    
        public JsonResult GenerateSHA512StringFors2s(string inputString) //Addded_NM
        {
            using (SHA512 sha512Hash = SHA512.Create())
            {
                //From String to byte array
                byte[] sourceBytes = Encoding.UTF8.GetBytes(inputString);
                byte[] hashBytes = sha512Hash.ComputeHash(sourceBytes);
                string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);
                SHA512Managed sha512 = new SHA512Managed();
                Byte[] encryptedSHA512 = sha512.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hash));
                sha512.Clear();
                var bts = Convert.ToBase64String(encryptedSHA512);
                //return Json(hash, JsonRequestBehavior.AllowGet); //Added_N
                return Json(hash);
            }
        }
        //action displaying notification (warning) to a store owner about inaccurate Worldline rounding
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> RoundingWarning(bool passProductNamesAndTotals)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prices and total aren't rounded, so display warning
            if (passProductNamesAndTotals && !_shoppingCartSettings.RoundPricesDuringCalculation)
                return Json(new { Result = await _localizationService.GetResourceAsync("Plugins.Payments.WorldlineStandard.RoundingWarning") });

            return Json(new { Result = string.Empty });
        }

        public async Task<IActionResult> PDTHandler()
        {
            var tx = _webHelper.QueryString<string>("tx");

            if (await _paymentPluginManager.LoadPluginBySystemNameAsync("Payments.Worldline") is not WorldlinePaymentProcessor processor || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("Worldline Standard module cannot be loaded");

            var (result, values, response) = await processor.GetPdtDetailsAsync(tx);

            if (result)
            {
                values.TryGetValue("custom", out var orderNumber);
                var orderNumberGuid = Guid.Empty;
                try
                {
                    orderNumberGuid = new Guid(orderNumber);
                }
                catch
                {
                    // ignored
                }

                var order = await _orderService.GetOrderByGuidAsync(orderNumberGuid);

                if (order == null)
                    return RedirectToAction("Index", "Home", new { area = string.Empty });

                var mcGross = decimal.Zero;

                try
                {
                    mcGross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
                }
                catch (Exception exc)
                {
                    await _logger.ErrorAsync("Worldline PDT. Error getting mc_gross", exc);
                }

                values.TryGetValue("payer_status", out var payerStatus);
                values.TryGetValue("payment_status", out var paymentStatus);
                values.TryGetValue("pending_reason", out var pendingReason);
                values.TryGetValue("mc_currency", out var mcCurrency);
                values.TryGetValue("txn_id", out var txnId);
                values.TryGetValue("payment_type", out var paymentType);
                values.TryGetValue("payer_id", out var payerId);
                values.TryGetValue("receiver_id", out var receiverId);
                values.TryGetValue("invoice", out var invoice);
                values.TryGetValue("mc_fee", out var mcFee);

                var sb = new StringBuilder();
                sb.AppendLine("Worldline PDT:");
                sb.AppendLine("mc_gross: " + mcGross);
                sb.AppendLine("Payer status: " + payerStatus);
                sb.AppendLine("Payment status: " + paymentStatus);
                sb.AppendLine("Pending reason: " + pendingReason);
                sb.AppendLine("mc_currency: " + mcCurrency);
                sb.AppendLine("txn_id: " + txnId);
                sb.AppendLine("payment_type: " + paymentType);
                sb.AppendLine("payer_id: " + payerId);
                sb.AppendLine("receiver_id: " + receiverId);
                sb.AppendLine("invoice: " + invoice);
                sb.AppendLine("mc_fee: " + mcFee);

                var newPaymentStatus = WorldlineHelper.GetPaymentStatus(paymentStatus, string.Empty);
                sb.AppendLine("New payment status: " + newPaymentStatus);

                //order note
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = order.Id,
                    Note = sb.ToString(),
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

                //validate order total
                var orderTotalSentToWorldline = await _genericAttributeService.GetAttributeAsync<decimal?>(order, WorldlineHelper.OrderTotalSentToWorldline);
                if (orderTotalSentToWorldline.HasValue && mcGross != orderTotalSentToWorldline.Value)
                {
                    var errorStr = $"Worldline PDT. Returned order total {mcGross} doesn't equal order total {order.OrderTotal}. Order# {order.Id}.";
                    //log
                    await _logger.ErrorAsync(errorStr);
                    //order note
                    await _orderService.InsertOrderNoteAsync(new OrderNote
                    {
                        OrderId = order.Id,
                        Note = errorStr,
                        DisplayToCustomer = false,
                        CreatedOnUtc = DateTime.UtcNow
                    });

                    return RedirectToAction("Index", "Home", new { area = string.Empty });
                }

                //clear attribute
                if (orderTotalSentToWorldline.HasValue)
                    await _genericAttributeService.SaveAttributeAsync<decimal?>(order, WorldlineHelper.OrderTotalSentToWorldline, null);

                if (newPaymentStatus != PaymentStatus.Paid)
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

                if (!_orderProcessingService.CanMarkOrderAsPaid(order))
                    return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });

                //mark order as paid
                order.AuthorizationTransactionId = txnId;
                await _orderService.UpdateOrderAsync(order);
                await _orderProcessingService.MarkOrderAsPaidAsync(order);

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
            else
            {
                if (!values.TryGetValue("custom", out var orderNumber))
                    orderNumber = _webHelper.QueryString<string>("cm");

                var orderNumberGuid = Guid.Empty;

                try
                {
                    orderNumberGuid = new Guid(orderNumber);
                }
                catch
                {
                    // ignored
                }

                var order = await _orderService.GetOrderByGuidAsync(orderNumberGuid);
                if (order == null)
                    return RedirectToAction("Index", "Home", new { area = string.Empty });

                //order note
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = order.Id,
                    Note = "Worldline PDT failed. " + response,
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                });

                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
        }

        public async Task<IActionResult> IPNHandler()
        {
            await using var stream = new MemoryStream();
            await Request.Body.CopyToAsync(stream);
            var strRequest = Encoding.ASCII.GetString(stream.ToArray());

            if (await _paymentPluginManager.LoadPluginBySystemNameAsync("Payments.Worldline") is not WorldlinePaymentProcessor processor || !_paymentPluginManager.IsPluginActive(processor))
                throw new NopException("Worldline Standard module cannot be loaded");

            var (result, values) = await processor.VerifyIpnAsync(strRequest);

            if (!result)
            {
                await _logger.ErrorAsync("Worldline IPN failed.", new NopException(strRequest));

                //nothing should be rendered to visitor
                return Ok();
            }

            var mcGross = decimal.Zero;

            try
            {
                mcGross = decimal.Parse(values["mc_gross"], new CultureInfo("en-US"));
            }
            catch
            {
                // ignored
            }

            values.TryGetValue("payment_status", out var paymentStatus);
            values.TryGetValue("pending_reason", out var pendingReason);
            values.TryGetValue("txn_id", out var txnId);
            values.TryGetValue("txn_type", out var txnType);
            values.TryGetValue("rp_invoice_id", out var rpInvoiceId);

            var sb = new StringBuilder();
            sb.AppendLine("Worldline IPN:");
            foreach (var kvp in values)
            {
                sb.AppendLine(kvp.Key + ": " + kvp.Value);
            }

            var newPaymentStatus = WorldlineHelper.GetPaymentStatus(paymentStatus, pendingReason);
            sb.AppendLine("New payment status: " + newPaymentStatus);

            var ipnInfo = sb.ToString();

            switch (txnType)
            {
                case "recurring_payment":
                    await ProcessRecurringPaymentAsync(rpInvoiceId, newPaymentStatus, txnId, ipnInfo);
                    break;
                case "recurring_payment_failed":
                    if (Guid.TryParse(rpInvoiceId, out var orderGuid))
                    {
                        var order = await _orderService.GetOrderByGuidAsync(orderGuid);
                        if (order != null)
                        {
                            var recurringPayment = (await _orderService.SearchRecurringPaymentsAsync(initialOrderId: order.Id))
                                .FirstOrDefault();
                            //failed payment
                            if (recurringPayment != null)
                                await _orderProcessingService.ProcessNextRecurringPaymentAsync(recurringPayment,
                                    new ProcessPaymentResult
                                    {
                                        Errors = new[] { txnType },
                                        RecurringPaymentFailed = true
                                    });
                        }
                    }

                    break;
                default:
                    values.TryGetValue("custom", out var orderNumber);
                    await ProcessPaymentAsync(orderNumber, ipnInfo, newPaymentStatus, mcGross, txnId);

                    break;
            }

            //nothing should be rendered to visitor
            return Ok();
        }

        public async Task<IActionResult> CancelOrder()
        {
            var store = await _storeContext.GetCurrentStoreAsync();
            var customer = await _workContext.GetCurrentCustomerAsync();
            var order = (await _orderService.SearchOrdersAsync(store.Id,
                customerId: customer.Id, pageSize: 1)).FirstOrDefault();

            if (order != null)
                return RedirectToRoute("OrderDetails", new { orderId = order.Id });

            return RedirectToRoute("Homepage");
        }

        #endregion
    }
}