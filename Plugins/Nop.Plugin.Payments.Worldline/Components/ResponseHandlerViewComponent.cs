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
using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Nop.Plugin.Payments.Worldline.Components
{
    [ViewComponent(Name = "ResponseHandler")]
    public class ResponseHandlerViewComponent : NopViewComponent
    {
        private IHostingEnvironment _env;
        //  private readonly IShoppingCartModelFactory = Nop.Web.Factories.IProductModelFactory;
        private readonly IShoppingCartModelFactory _shoppingCartModelFactory;
        private readonly IShoppingCartService _shoppingCartService;
        private readonly IStoreContext _storeContext;
        private readonly IWorkContext _workContext;

        public ResponseHandlerViewComponent(IHostingEnvironment env, IShoppingCartModelFactory shoppingCartModelFactory, IShoppingCartService shoppingCartService,
            IStoreContext storeContext,
            IWorkContext workContext)
        {
            _env = env;
            _shoppingCartModelFactory = shoppingCartModelFactory;
            _shoppingCartService = shoppingCartService;
            _storeContext = storeContext;
            _workContext = workContext;
        }
        public async Task<IViewComponentResult> InvokeAsync(IFormCollection formCollection)
        {
       
            try
            {
                foreach (var key in formCollection.Keys)
                {
                    var value = formCollection[key];
                }

                string path = _env.WebRootPath;

                string json = "";


                using (StreamReader r = new StreamReader(path + "\\output.json"))
                {
                    json = r.ReadToEnd();

                    r.Close();

                }
                JObject config_data = JObject.Parse(json);
                var data = formCollection["msg"].ToString().Split('|');
                if (data == null)
                {//|| data[1].ToString()== "User Aborted"
                    ViewBag.abrt = true;
                    //return Redirect(ControllerContext.HttpContext.Request.UrlReferrer.ToString());
                    //string referer = Request.Headers["Referer"].ToString();
                    //RequestHeaders header = Request.GetTypedHeaders();
                    //Uri uriReferer = header.Referer;
                    string referer = Request.Headers["Referer"].ToString();
                   // return Redirect(referer);
                }
                ViewBag.online_transaction_msg = data;
                if (data[0] == "0300")
                {
                    ViewBag.abrt = false;

                    var strJ = new
                    {
                        merchant = new
                        {
                            identifier = config_data["merchantCode"].ToString()
                        },
                        transaction = new
                        {
                            deviceIdentifier = "S",
                            currency = config_data["currency"],
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
                    HttpResponseMessage response = await client.PostAsync("https://www.paynimo.com/api/paynimoV2.req", content);
                    var a = response.Content.ReadAsStringAsync();

                    JObject dual_verification_result = JObject.Parse(JsonConvert.SerializeObject(a));
                    var jsonData = JObject.Parse(dual_verification_result["Result"].ToString()).Children();

                    List<JToken> tokens = jsonData.Children().ToList();

                    var jsonData1 = JObject.Parse(tokens[6].ToString()).Children();
                    List<JToken> tokens1 = jsonData.Children().ToList();
                    ViewBag.dual_verification_result = dual_verification_result;
                    ViewBag.a = a;
                    ViewBag.jsonData = jsonData;
                    ViewBag.tokens = tokens;
                    ViewBag.paramsData = formCollection["msg"];

                    // return response;
                }

            }
            catch (Exception ex)
            {

                //throw;
            }

            return View("~/Plugins/Payments.Worldline/Views/ResponseHandler.cshtml");
            //   return Content("Success");
            //return View("~/Plugins/Payments.Worldline/Views/PaymentInfo.cshtml");
            //  return Redirect(_storeContext.CurrentStore.Url+ "checkout/OpcSavePaymentInfo");
        
          
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
