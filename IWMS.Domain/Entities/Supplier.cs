using IWMS.Domain.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace IWMS.Domain.Entities
{
    public class Supplier:BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string ContactPerson { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public List<PurchaseOrder> PurchaseOrders { get; set; } = new();
    }
}
