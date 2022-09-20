using System;
using System.Collections.Generic;
using Nop.Core.Domain.Orders;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Worldline.Models
{
    public record RefundModel : BaseNopModel
    {
        public string TransactionId { get; set; }
        public string Amount { get; set; }
        public string TransactionDate { get; set; }
        public string OrderId { get; set; }
    }
}