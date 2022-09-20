using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Worldline.Infrastructure
{
    public partial class RouteProvider : IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="endpointRouteBuilder">Route builder</param>
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Worldline.ResponseHandler", "Plugins/Worldline/ResponseHandler",
                 new { controller = "Worldline", action = "ResponseHandler" });

            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Worldline.PaymentCheckout", "Plugins/Worldline/PaymentCheckout",
                new { controller = "Worldline", action = "PaymentCheckout" });

            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Worldline.Refund", "Admin/Worldline/Refund",
                 new { area= "Admin", controller = "Worldline", action = "Refund" });
            //PDT
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Worldline.PDTHandler", "Plugins/Worldline/PDTHandler",
                 new { controller = "Worldline", action = "PDTHandler" });

            //IPN
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Worldline.IPNHandler", "Plugins/Worldline/IPNHandler",
                 new { controller = "Worldline", action = "IPNHandler" });

            //Cancel
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Worldline.CancelOrder", "Plugins/Worldline/CancelOrder",
                 new { controller = "Worldline", action = "CancelOrder" });
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority => -1;
    }
}