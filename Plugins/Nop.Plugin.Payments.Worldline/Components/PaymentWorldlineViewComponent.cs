using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;
using Nop.Core.Domain.Orders;
using Nop.Services.Orders;
using Nop.Core;
using Nop.Web;
using Nop.Web.Factories;
using System.IO;
using Newtonsoft.Json.Linq;

using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Nop.Services.Configuration;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Worldline.Components
{
    [ViewComponent(Name = "PaymentWorldline")]
    public class PaymentWorldlineViewComponent : NopViewComponent
    {
        private IWebHostEnvironment _env;
      //  private readonly IShoppingCartModelFactory = Nop.Web.Factories.IProductModelFactory;
        private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;
        private readonly ISettingService _settingService;

        public PaymentWorldlineViewComponent(IWebHostEnvironment env,
            IShoppingCartModelFactory shoppingCartModelFactory, 
            IShoppingCartService shoppingCartService,
            IStoreContext storeContext, 
            ISettingService settingService,
            IWorkContext workContext)
        {
            _env = env;
            _shoppingCartModelFactory = shoppingCartModelFactory;
            _shoppingCartService = shoppingCartService;
            _settingService = settingService;
            _storeContext = storeContext;
            _workContext = workContext;
        }
        public async Task<IViewComponentResult> InvokeAsync()
        {
            string path = _env.WebRootPath;
            string tranId = GenerateRandomString(12);
            ViewBag.tranId = tranId;

            ViewBag.debitStartDate = DateTime.Now.ToString("yyyy-MM-dd");
            int year = Convert.ToInt32(DateTime.Now.Year.ToString());
            DateTime date = DateTime.Now;
            var enddate = date.AddYears(30);
            ViewBag.debitEndDate = enddate.ToString("yyyy-MM-dd");

            using (StreamReader r = new StreamReader(path + "//output.json"))
            {
                string json = r.ReadToEnd();

                ViewBag.config_data = json;
                var jsonData = JObject.Parse(json).Children();
                List<JToken> tokens = jsonData.Children().ToList();
                if (Convert.ToBoolean(tokens[25]) == true)
                {
                    if (Convert.ToBoolean(tokens[34]) == true)
                    {
                        ViewBag.enbSi = Convert.ToBoolean(tokens[25]);
                    }
                    else
                    {
                        ViewBag.enbSi = false;
                    }
                }
                else
                {
                    ViewBag.enbSi = false;
                }
            }

            var cart = await _shoppingCartService.GetShoppingCartAsync(await _workContext.GetCurrentCustomerAsync(), ShoppingCartType.ShoppingCart, _storeContext.GetCurrentStoreAsync().Result.Id);
            var model = _shoppingCartModelFactory.PrepareOrderTotalsModelAsync(cart, false);

            ViewBag.merchantcode =  _settingService.GetSettingAsync("worldlinepaymentsettings.merchantcode").Result.Value.ToString();
            ViewBag.currency = _settingService.GetSettingAsync("worldlinepaymentsettings.currency").Result.Value.ToString();
            ViewBag.SALT = _settingService.GetSettingAsync("worldlinepaymentsettings.SALT").Result.Value.ToString();

            ViewBag.paymentMode = _settingService.GetSettingAsync("worldlinepaymentsettings.paymentMode").Result.Value.ToString();
            ViewBag.paymentModeOrder = _settingService.GetSettingAsync("worldlinepaymentsettings.paymentModeOrder").Result.Value.ToString();
            //ViewBag.checkoutElement = _settingService.GetSettingAsync("worldlinepaymentsettings.checkoutElement").Value.ToString();
            ViewBag.merchantLogoUrl = _settingService.GetSettingAsync("worldlinepaymentsettings.logoURL").Result.Value.ToString();
            ViewBag.merchantMsg = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantMessage").Result.Value.ToString();
            ViewBag.disclaimerMsg = _settingService.GetSettingAsync("worldlinepaymentsettings.disclaimerMessage").Result.Value.ToString();

            ViewBag.primaryColor = _settingService.GetSettingAsync("worldlinepaymentsettings.primaryColor").Result.Value.ToString();
            ViewBag.secondaryColor = _settingService.GetSettingAsync("worldlinepaymentsettings.secondaryColor").Result.Value.ToString();
            ViewBag.buttonColor1 = _settingService.GetSettingAsync("worldlinepaymentsettings.buttonColor1").Result.Value.ToString();
            ViewBag.buttonColor2 = _settingService.GetSettingAsync("worldlinepaymentsettings.buttonColor2").Result.Value.ToString();
            ViewBag.merchantSchemeCode = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantSchemeCode").Result.Value.ToString();
            ViewBag.showPGResponseMsg = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.showPGResponseMsg").Result.Value.ToString().ToLower());
            ViewBag.enableAbortResponse = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.enableAbortResponse").Result.Value.ToString().ToLower());
            ViewBag.enableExpressPay = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.enableExpressPay").Result.Value.ToString().ToLower());
            ViewBag.enableNewWindowFlow = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.enableNewWindowFlow").Result.Value.ToString().ToLower());
            ViewBag.enableDebitDay = _settingService.GetSettingAsync("worldlinepaymentsettings.merchantSchemeCode").Result.Value.ToString();
            ViewBag.siDetailsAtMerchantEnd = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.siDetailsAtMerchantEnd").Result.Value.ToString().ToLower());
            ViewBag.enableSI = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.enableSI").Result.Value.ToString().ToLower());
            ViewBag.embedPaymentGatewayOnPage = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.embedPaymentGatewayOnPage").Result.Value.ToString().ToLower());
            ViewBag.separateCardMode = Convert.ToBoolean(_settingService.GetSettingAsync("worldlinepaymentsettings.separateCardMode").Result.Value.ToString().ToLower());

            if (_settingService.GetSettingAsync("worldlinepaymentsettings.merchantSchemeCode").Result.Value.ToString().ToLower() == "test")
            {
                ViewBag.ordTtl = "1.00";
            }
            else
            {
                //ViewBag.ordTtl = "10.00";
                ViewBag.ordTtl = model.Result.OrderTotal.Substring(1).Trim();
            }

            ViewBag.custId = _workContext.GetCurrentCustomerAsync().Id;

            return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
        }
        public string GenerateRandomString(int size)
        {
            var temp = Guid.NewGuid().ToString().Replace("-", string.Empty);
            var barcode = Regex.Replace(temp, "[a-zA-Z]", string.Empty).Substring(0, 10);

            return barcode.ToString();
        }
        //public JsonResult GenerateSHA512String(string inputString)
        //{
        //    using (SHA512 sha512Hash = SHA512.Create())
        //    {
        //        //From String to byte array
        //        byte[] sourceBytes = Encoding.UTF8.GetBytes(inputString);
        //        byte[] hashBytes = sha512Hash.ComputeHash(sourceBytes);
        //        string hash = BitConverter.ToString(hashBytes).Replace("-", String.Empty);

        //        System.Security.Cryptography.SHA512Managed sha512 = new System.Security.Cryptography.SHA512Managed();

        //        Byte[] EncryptedSHA512 = sha512.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hash));

        //        sha512.Clear();

        //        var bts = Convert.ToBase64String(EncryptedSHA512);

        //        //return Json(hash, JsonRequestBehavior.AllowGet);
        //        return Json(hash, new Newtonsoft.Json.JsonSerializerSettings());
        //    }
        //}
    }
}
